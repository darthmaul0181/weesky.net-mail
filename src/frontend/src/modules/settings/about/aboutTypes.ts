/** `GET /api/Version`: the running API's own version, reported apart from the web app's. */
export interface ServerVersion {
  version: string
  /** Absent when the build carried no commit. */
  commit?: string
}
