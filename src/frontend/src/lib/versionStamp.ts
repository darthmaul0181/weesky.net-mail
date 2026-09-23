/** A version is -dev unless this is a release build. Here, not in vite.config.js, so the suite can
 * test it: Vite's own dependencies refuse to load inside jsdom. */
export function versionStamp(version: string, release: boolean): string {
  return release ? version : `${version}-dev`
}
