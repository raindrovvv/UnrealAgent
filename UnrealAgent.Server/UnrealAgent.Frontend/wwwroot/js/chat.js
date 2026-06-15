/** Blazor에서 호출 — 지정된 code 요소에 highlight.js를 적용합니다. */
function highlightCode(element)
{
    if (element && window.hljs)
    {
        hljs.highlightElement(element);
    }
}

/** 채팅창 하단으로 스크롤합니다. */
function scrollToBottom()
{
    const el = document.getElementById('messages-bottom');
    if (el) el.scrollIntoView({ behavior: 'instant' });
}

/**
 * textarea에 키보드 핸들러를 등록합니다.
 * Enter → 전송 (dotnet 콜백), Shift+Enter → 줄바꿈 (기본 동작 허용)
 */
function setupChatInput(textarea, dotnet)
{
    textarea.addEventListener('keydown', (e) =>
    {
        if (e.key === 'Enter' && !e.shiftKey)
        {
            e.preventDefault();
            dotnet.invokeMethodAsync('SubmitFromJs');
        }
    });
}

const materialIconFallbacks = new Map(Object.entries({
    arrow_upward: "↑",
    attach_file: "+",
    autorenew: "↻",
    cancel: "×",
    check: "✓",
    check_circle: "✓",
    close: "×",
    description: "□",
    error: "!",
    expand_more: "⌄",
    folder: "▣",
    language: "⌁",
    neurology: "◌",
    person: "○",
    psychology: "◎",
    replay: "↻",
    settings: "⚙",
    stop: "■",
    terminal: ">",
    timer: "◷",
    verified_user: "◇",
    visibility: "◉"
}));

function applyMaterialIconFallbacks(root = document)
{
    const icons = root.querySelectorAll?.('.material-symbols-outlined') ?? [];
    icons.forEach((icon) =>
    {
        const key = (icon.getAttribute('data-icon-name') || icon.textContent || '').trim();
        if (!key || icon.getAttribute('data-icon-ready') === key) return;

        icon.setAttribute('data-icon-name', key);
        icon.textContent = materialIconFallbacks.get(key) || key.substring(0, 1).toUpperCase();
        icon.setAttribute('data-icon-ready', key);
        icon.setAttribute('aria-hidden', 'true');
    });
}

document.addEventListener('DOMContentLoaded', () =>
{
    applyMaterialIconFallbacks();

    const observer = new MutationObserver((mutations) =>
    {
        for (const mutation of mutations)
        {
            mutation.addedNodes.forEach((node) =>
            {
                if (node.nodeType !== Node.ELEMENT_NODE) return;
                if (node.classList?.contains('material-symbols-outlined')) applyMaterialIconFallbacks(node.parentElement || document);
                else applyMaterialIconFallbacks(node);
            });
        }
    });

    observer.observe(document.body, { childList: true, subtree: true });
});
