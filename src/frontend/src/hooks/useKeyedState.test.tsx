import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { useLayoutEffect } from 'react'
import { useKeyedState } from './useKeyedState'

function Probe({ k, committed }: { k: string; committed: number[] }) {
  const [value, setValue] = useKeyedState(() => 0, k)
  useLayoutEffect(() => { committed.push(value) })
  useLayoutEffect(() => { if (k === 'a') setValue(5) }, [k, setValue])
  return null
}

function FnProbe({ k, committed }: { k: string; committed: Array<() => string> }) {
  const [value] = useKeyedState<() => string>(() => () => 'a', k)
  useLayoutEffect(() => { committed.push(value) })
  return null
}

describe('useKeyedState', () => {
  it('never commits the previous key\'s value', () => {
    const committed: number[] = []
    const { rerender } = render(<Probe k="a" committed={committed} />)
    expect(committed[committed.length - 1]).toBe(5)
    committed.length = 0
    rerender(<Probe k="b" committed={committed} />)
    expect(committed).toEqual([0])
  })

  it('keeps its value while the key holds', () => {
    const committed: number[] = []
    const { rerender } = render(<Probe k="a" committed={committed} />)
    rerender(<Probe k="a" committed={committed} />)
    expect(committed[committed.length - 1]).toBe(5)
  })

  it('commits the fresh function itself on a key change, not its call result', () => {
    const committed: Array<() => string> = []
    const { rerender } = render(<FnProbe k="a" committed={committed} />)
    rerender(<FnProbe k="b" committed={committed} />)
    const last = committed[committed.length - 1]
    expect(typeof last).toBe('function')
    expect(last()).toBe('a')
  })
})
