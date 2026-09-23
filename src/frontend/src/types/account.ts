/** `POST /api/Login`: the session cookie's lifetime, in seconds. The cookie itself is HttpOnly. */
export interface LoginResponse {
  expiresIn: number
}

/** `GET /api/Account/Quota` and `GET /api/Admin/users/{id}/quota`. */
export interface Quota {
  storageBytesUsed: number
  storageBytesLimit: number
  messageCount: number
  messageLimit: number
}
