# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What's new

Every user-visible feature plans its "What's new" entry, unless the owner says it does not deserve one:
an entry in `src/frontend/src/modules/whatsnew/catalog.ts` (version = the `src/frontend/VERSION` that
ships it, bumped in the same branch), `items.<id>.title`/`body` in `locales/{en,fr}/whatsNew.json`,
a `WhatsNewDot` on each anchor of its path and a `WhatsNewTarget` around its block.
`modules/whatsnew/catalog.test.ts` fails the build if any of these is missing.
