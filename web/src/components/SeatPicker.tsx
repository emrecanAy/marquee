import { useMemo, useState } from 'react'
import { ApiError } from '../api/client'
import { useHoldSeats } from '../api/queries'
import type { Reservation, Seat } from '../api/types'
import { formatMoney } from '../lib/format'
import { sortLabels, toRows, totalPrice } from '../lib/seats'
import './SeatPicker.css'

const MAX_SEATS = 6

interface SeatPickerProps {
  showtimeId: string
  seats: Seat[]
  onHeld: (reservation: Reservation) => void
  onAdmissionLost: () => void
}

export function SeatPicker({ showtimeId, seats, onHeld, onAdmissionLost }: SeatPickerProps) {
  // Seats this visitor already holds (after coming back from checkout) start out selected.
  const [picked, setPicked] = useState(
    () => new Set(seats.filter((seat) => seat.status === 'mine').map((seat) => seat.label)),
  )
  const [hint, setHint] = useState<string>()
  const hold = useHoldSeats(showtimeId)

  const rows = useMemo(() => toRows(seats), [seats])
  // The map refreshes while the visitor is choosing. A picked seat that someone else took in the
  // meantime silently drops out of the selection instead of failing later at checkout.
  const selected = useMemo(() => {
    const selectable = new Set(seats.filter(isSelectable).map((seat) => seat.label))
    return new Set([...picked].filter((label) => selectable.has(label)))
  }, [picked, seats])

  function toggle(seat: Seat) {
    setHint(undefined)
    hold.reset()
    const next = new Set(selected)
    if (next.has(seat.label)) {
      next.delete(seat.label)
    } else if (next.size >= MAX_SEATS) {
      setHint(`One order can hold up to ${MAX_SEATS} seats.`)
      return
    } else {
      next.add(seat.label)
    }
    setPicked(next)
  }

  function holdSelected() {
    hold.mutate(sortLabels(selected), {
      onSuccess: onHeld,
      onError: (error) => {
        if (error instanceof ApiError && error.code === 'not_admitted') onAdmissionLost()
      },
    })
  }

  const labels = sortLabels(selected)
  const message = hold.error?.message ?? hint

  return (
    <div className="picker">
      <div className="picker__hall">
        <div className="picker__screen" aria-hidden="true">
          <span>Screen</span>
        </div>

        <div className="picker__rows" role="group" aria-label="Seats">
          {rows.map(({ row, seats: rowSeats }) => (
            <div key={row} className="picker__row">
              <span className="picker__row-label" aria-hidden="true">
                {row}
              </span>
              {rowSeats.map((seat, index) => (
                <button
                  key={seat.label}
                  type="button"
                  className={seatClass(seat, selected.has(seat.label), index === Math.floor(rowSeats.length / 2))}
                  aria-pressed={selected.has(seat.label)}
                  aria-label={describe(seat)}
                  disabled={!isSelectable(seat)}
                  onClick={() => toggle(seat)}
                >
                  {seat.number}
                </button>
              ))}
              <span className="picker__row-label" aria-hidden="true">
                {row}
              </span>
            </div>
          ))}
        </div>

        <ul className="picker__legend">
          <li>
            <span className="seat seat--sample" /> Available
          </li>
          <li>
            <span className="seat seat--sample seat--premium" /> Premium row
          </li>
          <li>
            <span className="seat seat--sample seat--selected" /> Your choice
          </li>
          <li>
            <span className="seat seat--sample seat--held" /> Being bought
          </li>
          <li>
            <span className="seat seat--sample seat--sold" /> Sold
          </li>
        </ul>
      </div>

      <div className="picker__summary" aria-live="polite">
        {labels.length === 0 ? (
          <p className="muted">Choose up to {MAX_SEATS} seats. They are held for you while you pay.</p>
        ) : (
          <p>
            <span className="picker__seats">{labels.join(', ')}</span>
            <span className="picker__total">{formatMoney(totalPrice(seats, selected))}</span>
          </p>
        )}
        {message && <p className="notice">{message}</p>}
        <button type="button" className="button" disabled={labels.length === 0 || hold.isPending} onClick={holdSelected}>
          {hold.isPending ? 'Holding seats' : labels.length > 1 ? `Hold ${labels.length} seats` : 'Hold seat'}
        </button>
      </div>
    </div>
  )
}

function isSelectable(seat: Seat): boolean {
  return seat.status === 'available' || seat.status === 'mine'
}

function seatClass(seat: Seat, selected: boolean, afterAisle: boolean): string {
  return [
    'seat',
    seat.category === 'premium' && 'seat--premium',
    selected ? 'seat--selected' : seat.status === 'held' ? 'seat--held' : seat.status === 'sold' && 'seat--sold',
    afterAisle && 'seat--after-aisle',
  ]
    .filter(Boolean)
    .join(' ')
}

function describe(seat: Seat): string {
  const state = seat.status === 'sold' ? 'sold' : seat.status === 'held' ? 'being bought by someone else' : formatMoney(seat.price)
  return `Row ${seat.row}, seat ${seat.number}, ${seat.category}, ${state}`
}
