/** A gap stands for the pages elided between two shown ones. */
export type PageItem = number | 'gap'

/** Beyond this many pages the strip is elided; below it, every page fits without gaps. */
const ALWAYS_SHOWN = 7

// Zero-indexed. The first and last pages are always offered, plus a window around the current one;
// the rest collapse into gaps, since forty buttons cannot fit a 380px column.
export function buildPageList(current: number, lastPage: number): PageItem[] {
  if (lastPage < 0) return []
  if (lastPage < ALWAYS_SHOWN) {
    return Array.from({ length: lastPage + 1 }, (_, index) => index)
  }

  const shown = new Set<number>([0, lastPage])
  for (let page = current - 1; page <= current + 1; page++) {
    if (page >= 0 && page <= lastPage) shown.add(page)
  }

  const sorted = [...shown].sort((a, b) => a - b)
  const items: PageItem[] = []

  sorted.forEach((page, index) => {
    // A gap earns its place only when it hides more than one page; hiding exactly one and
    // showing "…" in its stead costs the same width and takes away a click.
    // sorted[-1] at index 0 is undefined, which the condition itself excludes.
    const previous = sorted[index - 1]
    if (previous !== undefined && page - previous > 1) {
      items.push(page - previous === 2 ? page - 1 : 'gap')
    }
    items.push(page)
  })

  return items
}
