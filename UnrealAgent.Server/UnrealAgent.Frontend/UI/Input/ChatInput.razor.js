/**
 * textarea에 키 바인딩을 설정합니다.
 * Enter → 전송, Shift+Enter → 줄바꿈, Shift+Tab → 모드 순환.
 *
 * 한글/CJK IME 처리 전략:
 * - compositionstart/end로 조합 상태를 추적합니다.
 * - keyCode 229 (IME virtual key) Enter는 e.preventDefault() 없이 CEF에 완전 위임합니다.
 * - compositionend 후 32ms grace 타이머: 이 창에서 Enter가 눌리면 전송을 예약합니다.
 *
 * 한글 초성 유실 보정 (UE CEF IME 버그 우회):
 * UE 엔진의 FCEFTextInputMethodContext는 받침 분리 재조합(예: "핫" → "하" 커밋
 * + "세" 새 조합)을 CEF에 전달할 때 새 음절의 초성을 유실합니다.
 * 결과: "하세요" → "하ㅔ요". 엔진(런처 빌드)은 수정 불가하므로 JS에서 복원합니다.
 *
 * 감지 조건 (둘 다 만족해야 보정):
 *  1) 조합 커밋 시 직전 조합 상태에서 받침이 사라짐 ("핫" → "하" = ㅅ 유실 후보)
 *  2) 직후(150ms 내) 새 조합이 모음 단독 자모("ㅔ")로 시작
 * 보정: 그 조합이 모음 단독으로 끝나면 유실 초성과 합성해 치환 ("ㅔ" → "세").
 * 연쇄 보정: 보정 직후 홑자음이 단독 커밋되면 받침으로 병합 ("느" + "ㄴ" → "는").
 */

const MAX_IMAGE_BYTES = 8 * 1024 * 1024;
const NORMAL_IMAGE_EDGE = 2048;
const NORMAL_IMAGE_QUALITY = 0.86;
const FAST_VISION_IMAGE_EDGE = 1280;
const FAST_VISION_IMAGE_QUALITY = 0.82;
const ALLOWED_IMAGE_TYPES = new Set(["image/png", "image/jpeg"]);
const cleanupHandlers = new WeakMap();

// ── 한글 자모 합성/분해 ──

const CHO = [..."ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ"];
const JONG = ["", ..."ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ"];
// 겹받침 → [남는 받침, 분리되어 다음 초성이 되는 자음]
const JONG_SPLIT = {
    "ㄳ": ["ㄱ", "ㅅ"], "ㄵ": ["ㄴ", "ㅈ"], "ㄶ": ["ㄴ", "ㅎ"],
    "ㄺ": ["ㄹ", "ㄱ"], "ㄻ": ["ㄹ", "ㅁ"], "ㄼ": ["ㄹ", "ㅂ"],
    "ㄽ": ["ㄹ", "ㅅ"], "ㄾ": ["ㄹ", "ㅌ"], "ㄿ": ["ㄹ", "ㅍ"],
    "ㅀ": ["ㄹ", "ㅎ"], "ㅄ": ["ㅂ", "ㅅ"]
};

function decompose(ch)
{
    const code = ch.charCodeAt(0) - 0xAC00;
    if (code < 0 || code > 11171) return null;
    return { L: (code / 588) | 0, V: ((code / 28) | 0) % 21, T: code % 28 };
}

function compose(L, V, T)
{
    return String.fromCharCode(0xAC00 + (L * 21 + V) * 28 + T);
}

// 호환 자모 모음 ㅏ(0x314F)~ㅣ(0x3163)는 중성 인덱스 순서와 일치합니다.
function vowelIndex(ch)
{
    const code = ch.charCodeAt(0);
    return code >= 0x314F && code <= 0x3163 ? code - 0x314F : -1;
}

function isVowelJamo(s)
{
    return s.length === 1 && vowelIndex(s) >= 0;
}

function isConsonantJamo(s)
{
    if (s.length !== 1) return false;
    const code = s.charCodeAt(0);
    return code >= 0x3131 && code <= 0x314E;
}

/** 커밋으로 사라진 받침 자음을 반환합니다. 유실이 아니면 null. */
function detectLostBatchim(prevComp, endData)
{
    if (!prevComp || prevComp.length !== 1 || endData.length !== 1) return null;
    const prev = decompose(prevComp);
    const end = decompose(endData);
    if (!prev || !end || prev.L !== end.L || prev.V !== end.V) return null;

    const prevJong = JONG[prev.T];
    const endJong = JONG[end.T];

    // 홑받침이 통째로 사라짐: 핫 → 하 (ㅅ 유실)
    if (prev.T !== 0 && end.T === 0)
        return JONG_SPLIT[prevJong] ? null : prevJong;

    // 겹받침이 홑받침으로 줄어듦: 닭 → 달 (ㄱ 유실)
    const split = JONG_SPLIT[prevJong];
    if (split && split[0] === endJong)
        return split[1];

    return null;
}

export function setupKeyBindings(textarea, dotNetRef)
{
    cleanupKeyBindings(textarea);

    let isComposing = false;
    let pendingSubmit = false;
    let graceTimer = null;
    let disposed = false;
    let wasCommandInput = false;

    // ── 초성 유실 보정 상태 ──
    let lastUpdate = "";          // 현재 조합의 마지막 update 데이터
    let prevDistinctUpdate = "";  // lastUpdate와 다른 직전 update 데이터
    let firstUpdateSeen = false;  // 현재 조합의 첫 update 여부
    let lostCho = null;           // { jamo, time } — 받침 유실 후보
    let armed = false;            // 모음 단독 시작 확인됨 → 보정 대기
    let lastFix = null;           // { time } — 직전 보정 (홑자음 병합용)
    const handlers = [];

    function addListener(target, type, handler)
    {
        target.addEventListener(type, handler);
        handlers.push([target, type, handler]);
    }

    function clearGraceTimer()
    {
        if (graceTimer)
        {
            clearTimeout(graceTimer);
            graceTimer = null;
        }
    }

    cleanupHandlers.set(textarea, {
        handlers,
        clearGraceTimer,
        markDisposed: () =>
        {
            disposed = true;
        }
    });

    function syncCommandState()
    {
        const value = textarea.value ?? "";
        const isCommandInput = value.startsWith("/") && !value.includes(" ");

        if (isCommandInput || wasCommandInput)
        {
            wasCommandInput = isCommandInput;
            dotNetRef.invokeMethodAsync("HandleCommandInput", value);
        }
    }

    function doSubmit()
    {
        const value = textarea.value ?? "";
        dotNetRef.invokeMethodAsync("SubmitFromJs", value);
    }

    async function tryReadClipboardImage(e)
    {
        if (e.defaultPrevented)
            return;

        const form = textarea.closest("form");
        const activeElement = document.activeElement;
        if (e.currentTarget === document
            && activeElement !== textarea
            && (!form || !form.contains(activeElement)))
            return;

        if (attachFromDataTransferItems(e))
            return;

        const types = Array.from(e.clipboardData?.types ?? []);
        const hasText = types.includes("text/plain") || types.includes("text/html");
        const mayContainImage = types.includes("Files") ||
            types.some((type) => type.startsWith("image/")) ||
            types.length === 0;
        if (!mayContainImage || hasText)
            return;

        e.preventDefault();
        await attachFromClipboardApi();
    }

    function attachFromDataTransferItems(e)
    {
        const items = e.clipboardData?.items;
        if (!items)
            return false;

        for (let i = 0; i < items.length; i++)
        {
            const item = items[i];
            if (item.kind !== "file" || !item.type?.startsWith("image/"))
                continue;

            const mimeType = item.type || "image/png";
            if (!ALLOWED_IMAGE_TYPES.has(mimeType))
            {
                e.preventDefault();
                dotNetRef.invokeMethodAsync("HandleClipboardImageRejected", "PNG 또는 JPEG 이미지만 첨부할 수 있습니다.");
                continue;
            }

            const file = item.getAsFile();
            if (!file) continue;

            e.preventDefault();
            attachFile(mimeType, file);
            return true;
        }

        return false;
    }

    async function attachFromClipboardApi()
    {
        if (!navigator.clipboard || !navigator.clipboard.read)
        {
            await dotNetRef.invokeMethodAsync(
                "HandleClipboardImageRejected",
                "이 에디터 WebBrowser가 클립보드 이미지를 제공하지 않았습니다. 입력창을 클릭한 뒤 Ctrl+V를 다시 눌러 주세요.");
            return;
        }

        try
        {
            const clipboardItems = await navigator.clipboard.read();
            for (const clipboardItem of clipboardItems)
            {
                const mimeType = clipboardItem.types.find((type) => ALLOWED_IMAGE_TYPES.has(type));
                if (!mimeType)
                    continue;

                const blob = await clipboardItem.getType(mimeType);
                attachFile(mimeType, blob);
                return;
            }

            await dotNetRef.invokeMethodAsync(
                "HandleClipboardImageRejected",
                "클립보드에 PNG/JPEG 이미지가 없습니다. 스크린샷을 복사한 뒤 다시 붙여넣어 주세요.");
        }
        catch
        {
            await dotNetRef.invokeMethodAsync(
                "HandleClipboardImageRejected",
                "클립보드 이미지를 읽지 못했습니다. 입력창을 클릭한 뒤 Ctrl+V를 다시 눌러 주세요.");
        }
    }

    function attachFile(mimeType, file)
    {
        prepareAndAttachImage(mimeType, file);
    }

    async function prepareAndAttachImage(mimeType, file)
    {
        try
        {
            const bUseOriginalForNormal = file.size <= MAX_IMAGE_BYTES;
            const normalBlob = bUseOriginalForNormal
                ? file
                : await compressImage(file, NORMAL_IMAGE_EDGE, NORMAL_IMAGE_QUALITY);

            if (!normalBlob || normalBlob.size > MAX_IMAGE_BYTES)
            {
                dotNetRef.invokeMethodAsync("HandleClipboardImageRejected", makeSizeError(normalBlob?.size ?? file.size));
                return;
            }

            const fastBlob = await compressImage(file, FAST_VISION_IMAGE_EDGE, FAST_VISION_IMAGE_QUALITY);
            if (!fastBlob || fastBlob.size > MAX_IMAGE_BYTES)
            {
                dotNetRef.invokeMethodAsync("HandleClipboardImageRejected", makeSizeError(fastBlob?.size ?? file.size));
                return;
            }

            attachBlobs(bUseOriginalForNormal ? mimeType : "image/jpeg", normalBlob, "image/jpeg", fastBlob);
        }
        catch
        {
            dotNetRef.invokeMethodAsync(
                "HandleClipboardImageRejected",
                "클립보드 이미지를 첨부용으로 변환하지 못했습니다. 파일로 저장한 뒤 더 작은 영역을 캡처해 주세요.");
        }
    }

    async function compressImage(file, maxEdge, quality)
    {
        const bitmap = await createImageBitmap(file);
        const scale = Math.min(1, maxEdge / Math.max(bitmap.width, bitmap.height));
        const width = Math.max(1, Math.round(bitmap.width * scale));
        const height = Math.max(1, Math.round(bitmap.height * scale));
        const canvas = document.createElement("canvas");
        canvas.width = width;
        canvas.height = height;

        const context = canvas.getContext("2d");
        context.drawImage(bitmap, 0, 0, width, height);
        bitmap.close?.();

        return await canvasToBlob(canvas, "image/jpeg", quality);
    }

    async function attachBlobs(mimeType, blob, fastMimeType, fastBlob)
    {
        try
        {
            const base64 = await readBlobBase64(blob);
            const fastBase64 = await readBlobBase64(fastBlob);
            await dotNetRef.invokeMethodAsync(
                "HandleClipboardImage",
                mimeType,
                base64,
                fastMimeType,
                fastBase64);
        }
        catch
        {
            dotNetRef.invokeMethodAsync(
                "HandleClipboardImageRejected",
                "이미지를 첨부하지 못했습니다. 이미지가 너무 크면 작은 영역을 캡처해서 다시 붙여넣어 주세요.");
        }
    }

    function readBlobBase64(blob)
    {
        return new Promise((resolve, reject) =>
        {
            const reader = new FileReader();
            reader.onload = function ()
            {
                const result = String(reader.result ?? "");
                const commaIndex = result.indexOf(",");
                if (commaIndex < 0)
                {
                    reject(new Error("Invalid image data URL."));
                    return;
                }

                resolve(result.slice(commaIndex + 1));
            };
            reader.onerror = reject;
            reader.readAsDataURL(blob);
        });
    }

    function canvasToBlob(canvas, mimeType, quality)
    {
        return new Promise((resolve) =>
        {
            canvas.toBlob(resolve, mimeType, quality);
        });
    }

    /** 커밋된 모음 단독 자모를 유실 초성과 합성해 치환합니다. */
    function applyChoFix(vowelData)
    {
        const pos = textarea.selectionStart - 1;
        if (pos < 0 || textarea.value[pos] !== vowelData) return;

        const choIdx = CHO.indexOf(lostCho.jamo);
        const vIdx = vowelIndex(vowelData);
        if (choIdx < 0 || vIdx < 0) return;

        textarea.setRangeText(compose(choIdx, vIdx, 0), pos, pos + 1, "end");
        lastFix = { time: performance.now() };
    }

    /** 보정 직후 단독 커밋된 홑자음을 직전 음절의 받침으로 병합합니다. */
    function applyBatchimMerge(consonant)
    {
        const pos = textarea.selectionStart - 1;
        if (pos < 1 || textarea.value[pos] !== consonant) return;

        const prev = decompose(textarea.value[pos - 1]);
        const tIdx = JONG.indexOf(consonant);
        if (!prev || prev.T !== 0 || tIdx <= 0) return;

        textarea.setRangeText(compose(prev.L, prev.V, tIdx), pos - 1, pos + 1, "end");
    }

    addListener(textarea, "compositionstart", () =>
    {
        isComposing = true;
        pendingSubmit = false;
        lastUpdate = "";
        prevDistinctUpdate = "";
        firstUpdateSeen = false;
        clearGraceTimer();
    });

    addListener(textarea, "compositionupdate", (e) =>
    {
        const data = e.data ?? "";

        if (!firstUpdateSeen)
        {
            firstUpdateSeen = true;
            // 받침 유실 직후 모음 단독으로 시작하는 조합 = 버그 발생 확정
            armed = lostCho !== null
                && performance.now() - lostCho.time < 150
                && isVowelJamo(data);
            if (!armed) lostCho = null;
        }

        if (data !== lastUpdate)
        {
            prevDistinctUpdate = lastUpdate;
            lastUpdate = data;
        }
    });

    addListener(textarea, "compositionend", (e) =>
    {
        isComposing = false;
        const endData = e.data ?? "";

        // 1) 보정 실행: 모음 단독 조합으로 끝났고 유실 초성이 대기 중이면 합성
        if (armed && lostCho && isVowelJamo(endData))
        {
            applyChoFix(endData);
        }
        // 2) 연쇄 병합: 보정 직후 홑자음 단독 커밋이면 받침으로 합침
        else if (lastFix && isConsonantJamo(endData)
            && performance.now() - lastFix.time < 2000)
        {
            applyBatchimMerge(endData);
            lastFix = null;
        }
        armed = false;
        lostCho = null;

        // 3) 다음 조합을 위한 받침 유실 후보 감지
        //    (커밋 직전 update가 endData와 같으면 그 이전 update와 비교)
        const prevComp = lastUpdate === endData ? prevDistinctUpdate : lastUpdate;
        const lost = detectLostBatchim(prevComp, endData);
        if (lost)
            lostCho = { jamo: lost, time: performance.now() };

        clearGraceTimer();

        // CEF에서 compositionend 직후 textarea.value 커밋에 약간의 시간이 필요합니다.
        graceTimer = setTimeout(() =>
        {
            if (disposed)
                return;

            graceTimer = null;
            syncCommandState();

            if (pendingSubmit)
            {
                pendingSubmit = false;
                doSubmit();
            }
        }, 32);
    });

    addListener(textarea, "input", function (e)
    {
        if (!isComposing && !e.isComposing)
            syncCommandState();
    });

    addListener(textarea, "paste", tryReadClipboardImage);
    addListener(document, "paste", tryReadClipboardImage);

    addListener(textarea, "keydown", function (e)
    {
        if (e.key === "Enter" && !e.shiftKey)
        {
            // keyCode 229 = IME virtual key: TSF 레이어에서 처리 중입니다.
            // preventDefault를 호출하면 CEF가 "웹 처리 완료"로 마킹해 조합이 깨집니다.
            if (e.keyCode === 229) return;

            e.preventDefault();

            const isImeKey = isComposing || e.isComposing;
            if (isImeKey)
            {
                pendingSubmit = true;
                return;
            }

            if (graceTimer)
            {
                pendingSubmit = true;
            }
            else
            {
                doSubmit();
            }
        }
        else if (e.key === "Tab" && e.shiftKey)
        {
            e.preventDefault();
            dotNetRef.invokeMethodAsync("CycleMode");
        }
        else if (e.key === "Escape" && wasCommandInput)
        {
            wasCommandInput = false;
            dotNetRef.invokeMethodAsync("PopupClose");
        }
    });
}

export function cleanupKeyBindings(textarea)
{
    if (!textarea)
        return;

    const binding = cleanupHandlers.get(textarea);
    if (!binding) return;

    binding.markDisposed();
    binding.clearGraceTimer();

    for (const [target, type, handler] of binding.handlers)
        target.removeEventListener(type, handler);

    cleanupHandlers.delete(textarea);
}

export function getValue(textarea)
{
    return textarea?.value ?? "";
}

export function clearValue(textarea)
{
    if (!textarea)
        return;

    textarea.value = "";
    textarea.dispatchEvent(new Event("input", { bubbles: true }));
    textarea.focus();
}

export function focus(textarea)
{
    textarea?.focus();
}

function makeSizeError(byteCount)
{
    const actualMb = byteCount / 1024 / 1024;
    return `클립보드 이미지가 너무 큽니다. 현재 ${actualMb.toFixed(1)}MB / 최대 8MB까지 첨부할 수 있습니다.`;
}
