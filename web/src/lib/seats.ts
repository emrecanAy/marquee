import type { Seat } from '../api/types'

export interface SeatRow {
  row: string
  seats: Seat[]
}

/** Groups a flat seat list into rows, front row first, seats left to right. */
export function toRows(seats: Seat[]): SeatRow[] {
  const rows = new Map<string, Seat[]>()
  for (const seat of seats) {
    const row = rows.get(seat.row)
    if (row) row.push(seat)
    else rows.set(seat.row, [seat])
  }

  return [...rows.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([row, rowSeats]) => ({ row, seats: rowSeats.sort((a, b) => a.number - b.number) }))
}

export function totalPrice(seats: Seat[], selected: ReadonlySet<string>): number {
  return seats.filter((seat) => selected.has(seat.label)).reduce((sum, seat) => sum + seat.price, 0)
}

/** Labels in reading order: A2 before A10 before B1. */
export function sortLabels(labels: Iterable<string>): string[] {
  return [...labels].sort((a, b) => a.localeCompare(b, undefined, { numeric: true }))
}
