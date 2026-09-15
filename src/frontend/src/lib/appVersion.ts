/** The web app's own build: the VERSION file (suffixed -dev outside production), its commit and
    when it was built. The API reports its own through GET /api/Version — the two deploy apart. */
export const WEB_VERSION: string = __APP_VERSION__
export const WEB_COMMIT: string | null = __APP_COMMIT__
export const BUILT_AT: string = __APP_BUILT_AT__

export function versionLabel(version: string, commit: string | null): string {
  return commit ? `${version} (${commit})` : version
}
