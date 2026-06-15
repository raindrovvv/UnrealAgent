const MAX_IMAGE_BYTES = 8 * 1024 * 1024;
const NORMAL_IMAGE_EDGE = 2048;
const NORMAL_IMAGE_QUALITY = 0.86;
const FAST_VISION_IMAGE_EDGE = 1280;
const FAST_VISION_IMAGE_QUALITY = 0.82;
const ALLOWED_IMAGE_TYPES = new Set(["image/png", "image/jpeg"]);

export async function attachFromClipboard(dotNetRef)
{
    if (!navigator.clipboard || !navigator.clipboard.read)
    {
        await reject(dotNetRef, "이 에디터에서는 파일 선택이 차단됩니다. 스크린샷을 복사한 뒤 입력창에 Ctrl+V로 붙여넣어 주세요.");
        return;
    }

    try
    {
        const items = await navigator.clipboard.read();
        for (const item of items)
        {
            const mimeType = item.types.find((type) => ALLOWED_IMAGE_TYPES.has(type));
            if (!mimeType)
                continue;

            const blob = await item.getType(mimeType);
            await prepareAndAttachBlob(dotNetRef, mimeType, blob);
            return;
        }

        await reject(dotNetRef, "클립보드에 PNG/JPEG 이미지가 없습니다. 스크린샷을 복사한 뒤 Ctrl+V로 붙여넣어 주세요.");
    }
    catch
    {
        await reject(dotNetRef, "클립보드 이미지를 직접 읽을 수 없습니다. 입력창에 Ctrl+V로 붙여넣어 주세요.");
    }
}

async function prepareAndAttachBlob(dotNetRef, mimeType, blob)
{
    if (!ALLOWED_IMAGE_TYPES.has(mimeType))
    {
        await reject(dotNetRef, "PNG 또는 JPEG 이미지만 첨부할 수 있습니다.");
        return;
    }

    await attachPreparedImages(dotNetRef, mimeType, blob);
}

async function attachPreparedImages(dotNetRef, mimeType, blob)
{
    try
    {
        const bUseOriginalForNormal = blob.size <= MAX_IMAGE_BYTES;
        const normalBlob = bUseOriginalForNormal
            ? blob
            : await compressImage(blob, NORMAL_IMAGE_EDGE, NORMAL_IMAGE_QUALITY);

        if (!normalBlob || normalBlob.size > MAX_IMAGE_BYTES)
        {
            await reject(dotNetRef, makeSizeError(normalBlob?.size ?? blob.size));
            return;
        }

        const fastBlob = await compressImage(blob, FAST_VISION_IMAGE_EDGE, FAST_VISION_IMAGE_QUALITY);
        if (!fastBlob || fastBlob.size > MAX_IMAGE_BYTES)
        {
            await reject(dotNetRef, makeSizeError(fastBlob?.size ?? blob.size));
            return;
        }

        await readBlobs(dotNetRef, bUseOriginalForNormal ? mimeType : "image/jpeg", normalBlob, "image/jpeg", fastBlob);
    }
    catch
    {
        await reject(dotNetRef, "클립보드 이미지를 첨부용으로 변환하지 못했습니다. 파일로 저장한 뒤 더 작은 영역을 캡처해 주세요.");
    }
}

async function compressImage(blob, maxEdge, quality)
{
    const bitmap = await createImageBitmap(blob);
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

async function readBlobs(dotNetRef, mimeType, blob, fastMimeType, fastBlob)
{
    try
    {
        const base64 = await readBlobBase64(blob);
        const fastBase64 = await readBlobBase64(fastBlob);
        await dotNetRef.invokeMethodAsync("OnImagePicked", mimeType, base64, fastMimeType, fastBase64);
    }
    catch
    {
        await reject(dotNetRef, "이미지를 첨부하지 못했습니다. 이미지가 너무 크면 작은 영역을 캡처해서 다시 붙여넣어 주세요.");
    }
}

function readBlobBase64(blob)
{
    return new Promise((resolve, rejectPromise) =>
    {
        const reader = new FileReader();
        reader.onload = function ()
        {
            const result = String(reader.result ?? "");
            const commaIndex = result.indexOf(",");
            if (commaIndex < 0)
            {
                rejectPromise(new Error("Invalid image data URL."));
                return;
            }

            resolve(result.slice(commaIndex + 1));
        };
        reader.onerror = rejectPromise;
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

function reject(dotNetRef, message)
{
    return dotNetRef.invokeMethodAsync("OnImageRejected", message);
}

function makeSizeError(byteCount)
{
    const actualMb = byteCount / 1024 / 1024;
    return `클립보드 이미지가 너무 큽니다. 현재 ${actualMb.toFixed(1)}MB / 최대 8MB까지 첨부할 수 있습니다.`;
}
