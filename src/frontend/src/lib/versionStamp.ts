/**
 * The one rule both halves of the build obey: a version is -dev unless this is a release build.
 * Written here rather than inline in vite.config.js so the suite can read it — a test cannot
 * import the config itself (Vite's own dependencies refuse to load inside jsdom).
 */
export function versionStamp(version: string, release: boolean): string {
  return release ? version : `${version}-dev`
}
