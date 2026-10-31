// Keep the deadline active while the response body is being consumed, too.
// An aborted mutation may have committed: callers must not replay it automatically.
export async function withEditorRequest(url, options, consume, timeoutMs = 45000) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    try {
        const response = await fetch(url, { ...options, signal: controller.signal });
        return await consume(response);
    } finally { clearTimeout(timer); }
}
