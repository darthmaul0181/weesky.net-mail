export interface Placed<T> {
  item: T
  /** 0-based, out of `columns`: the two together are the block's share of the day's width. */
  column: number
  columns: number
  top: number
  height: number
}

/** One day's dated events side by side: overlapping events form a cluster sharing its width, and
 * a freed column is reused (9–12, 9–10, 10–11 take two columns, not three). */
export function layoutColumn<T>(
  items: T[],
  startMinuteOf: (item: T) => number,
  endMinuteOf: (item: T) => number,
  pxPerHour: number,
  minHeight: number,
): Placed<T>[] {
  const sorted = [...items].sort((a, b) =>
    startMinuteOf(a) - startMinuteOf(b) || endMinuteOf(b) - endMinuteOf(a))

  const placed: Placed<T>[] = []
  let cluster: Placed<T>[] = []
  let columnEnds: number[] = []
  let clusterEnd = -Infinity

  const closeCluster = () => {
    for (const entry of cluster) entry.columns = columnEnds.length
    cluster = []
    columnEnds = []
  }

  for (const item of sorted) {
    const start = startMinuteOf(item)
    const end = endMinuteOf(item)

    if (start >= clusterEnd) {
      closeCluster()
      clusterEnd = end
    } else {
      clusterEnd = Math.max(clusterEnd, end)
    }

    let column = columnEnds.findIndex(free => free <= start)
    if (column === -1) column = columnEnds.push(start) - 1
    columnEnds[column] = end

    const entry: Placed<T> = {
      item, column, columns: 1,
      top: start * pxPerHour / 60,
      height: Math.max(minHeight, (end - start) * pxPerHour / 60),
    }
    cluster.push(entry)
    placed.push(entry)
  }

  closeCluster()
  return placed
}
