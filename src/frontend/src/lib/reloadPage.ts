// window.location cannot be redefined under the vmThreads pool, so a test mocks this module instead.
export const reloadPage = () => window.location.reload()
