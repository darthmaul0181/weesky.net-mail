import { useState } from 'react'

export interface LineListOptions<T> {
  blank: () => T
  /** Removing the last line leaves a blank one in its place rather than an empty list. */
  keepOne: boolean
  max: number
}

export interface LineListState<T> {
  lines: T[]
  update: (index: number, patch: Partial<T>) => void
  remove: (index: number) => void
  add: () => void
  canAdd: boolean
  replace: (lines: T[]) => void
}

export function useLineList<T>(initial: () => T[], { blank, keepOne, max }: LineListOptions<T>): LineListState<T> {
  const [lines, setLines] = useState(initial)
  return {
    lines,
    update: (index, patch) =>
      setLines(previous => previous.map((line, i) => (i === index ? { ...line, ...patch } : line))),
    remove: index => setLines(previous => {
      const next = previous.filter((_, i) => i !== index)
      return next.length === 0 && keepOne ? [blank()] : next
    }),
    add: () => setLines(previous => (previous.length < max ? [...previous, blank()] : previous)),
    canAdd: lines.length < max,
    replace: setLines,
  }
}
