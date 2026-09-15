import { BUILT_AT, WEB_COMMIT, WEB_VERSION, versionLabel } from './appVersion'
import { versionStamp } from './versionStamp'

describe('appVersion', () => {
  // The suite is not a release build, so it sees what a dev deployment ships.
  it('stamps the product version, suffixed -dev outside a release build', () => {
    expect(WEB_VERSION).toMatch(/^\d+\.\d+\.\d+-dev$/)
  })

  // Host-dependent: a checkout has a commit, an extracted tarball has none. Both are legitimate,
  // and a build made without one must not be a red suite.
  it('carries the short commit the bundle was built from, or none at all', () => {
    expect(WEB_COMMIT === null || /^[0-9a-f]{7}$/.test(WEB_COMMIT)).toBe(true)
  })

  it('stamps the build time as an ISO instant', () => {
    expect(new Date(BUILT_AT).toISOString()).toBe(BUILT_AT)
  })
})

// The VERSION file itself sits above this project and Vitest overrides `server.fs.allow`, so it
// cannot be read here; the API suite pins the file-to-version wiring (ProductVersionTests). What
// is pinned here is the switch the config applies to it — RELEASE_BUILD, the same one the API
// build reads as -p:ReleaseBuild, so the two halves cannot disagree about what a release is.
describe('versionStamp', () => {
  it('leaves a release build bare', () => {
    expect(versionStamp('1.1.0', true)).toBe('1.1.0')
  })

  it('marks everything else -dev', () => {
    expect(versionStamp('1.1.0', false)).toBe('1.1.0-dev')
  })
})

describe('versionLabel', () => {
  it('puts the commit in brackets after the version', () => {
    expect(versionLabel('1.0.0', 'a1b2c3d')).toBe('1.0.0 (a1b2c3d)')
  })

  // A build made outside a git checkout has no commit; brackets around nothing read as a fault.
  it('is the bare version when the build carried no commit', () => {
    expect(versionLabel('1.0.0', null)).toBe('1.0.0')
  })
})
