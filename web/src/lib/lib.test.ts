import { describe, expect, it } from 'vitest'
import type { Seat } from '../api/types'
import { formatCardNumber, tokenizeCard } from './cards'
import { formatCountdown, formatDuration, relativeDay } from './format'
import { sortLabels, toRows, totalPrice } from './seats'

const seat = (label: string, price = 100): Seat => ({
  label,
  row: label[0]!,
  number: Number(label.slice(1)),
  category: 'standard',
  price,
  status: 'available',
})

describe('seats', () => {
  it('groups seats into rows from the screen backwards, left to right', () => {
    const rows = toRows([seat('B2'), seat('A10'), seat('A2'), seat('B1')])

    expect(rows.map((row) => [row.row, row.seats.map((s) => s.label)])).toEqual([
      ['A', ['A2', 'A10']],
      ['B', ['B1', 'B2']],
    ])
  })

  it('totals only the selected seats', () => {
    const seats = [seat('A1', 180), seat('A2', 180), seat('F1', 252)]

    expect(totalPrice(seats, new Set(['A1', 'F1']))).toBe(432)
  })

  it('sorts labels in reading order', () => {
    expect(sortLabels(['B1', 'A10', 'A2'])).toEqual(['A2', 'A10', 'B1'])
  })
})

describe('cards', () => {
  it('maps the sandbox card numbers to their outcomes', () => {
    expect(tokenizeCard('4242 4242 4242 4242')).toEqual({ ok: true, token: 'tok_approved' })
    expect(tokenizeCard('4000-0000-0000-0002')).toEqual({ ok: true, token: 'tok_declined' })
  })

  it('rejects incomplete and mistyped numbers before anything is sent', () => {
    expect(tokenizeCard('4242 4242')).toMatchObject({ ok: false })
    expect(tokenizeCard('4242 4242 4242 4241')).toMatchObject({ ok: false })
  })

  it('formats the number in groups of four while typing', () => {
    expect(formatCardNumber('42424242424')).toBe('4242 4242 424')
    expect(formatCardNumber('4242-4242 4242x42421111')).toBe('4242 4242 4242 4242')
  })
})

describe('format', () => {
  it('counts down in minutes and seconds and stops at zero', () => {
    const now = Date.parse('2026-01-01T20:00:00Z')

    expect(formatCountdown('2026-01-01T20:07:42Z', now)).toBe('07:42')
    expect(formatCountdown('2026-01-01T19:59:00Z', now)).toBe('00:00')
  })

  it('describes near days the way a box office would', () => {
    const now = new Date(2026, 0, 1, 23, 30)

    expect(relativeDay(new Date(2026, 0, 1, 23, 45).toISOString(), now)).toBe('Today')
    expect(relativeDay(new Date(2026, 0, 2, 0, 15).toISOString(), now)).toBe('Tomorrow')
    expect(relativeDay(new Date(2026, 0, 4, 12, 0).toISOString(), now)).toBe('Sun')
  })

  it('formats running times', () => {
    expect(formatDuration(127)).toBe('2 h 07 min')
    expect(formatDuration(45)).toBe('45 min')
  })
})
