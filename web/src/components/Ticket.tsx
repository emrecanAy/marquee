import type { Reservation } from '../api/types'
import { formatDay, formatMoney, formatTime } from '../lib/format'
import './Ticket.css'

/**
 * A paper ticket. Before payment it is the order summary; once the reservation is confirmed the
 * stub carries the ticket code.
 */
export function Ticket({ reservation }: { reservation: Reservation }) {
  return (
    <div className="ticket">
      <div className="ticket__main">
        <p className="ticket__film">{reservation.movieTitle}</p>
        <dl className="ticket__facts">
          <div>
            <dt>When</dt>
            <dd>
              {formatDay(reservation.startsAt)}, {formatTime(reservation.startsAt)}
            </dd>
          </div>
          <div>
            <dt>Where</dt>
            <dd>{reservation.hallName}</dd>
          </div>
          <div>
            <dt>{reservation.seats.length > 1 ? 'Seats' : 'Seat'}</dt>
            <dd>{reservation.seats.join(', ')}</dd>
          </div>
        </dl>
      </div>

      <div className="ticket__stub">
        {reservation.ticketCode ? (
          <>
            <Barcode code={reservation.ticketCode} />
            <p className="ticket__code">{reservation.ticketCode}</p>
          </>
        ) : (
          <>
            <p className="ticket__stub-label">Total</p>
            <p className="ticket__total">{formatMoney(reservation.amount)}</p>
          </>
        )}
      </div>
    </div>
  )
}

/** Decorative stripes derived from the code, so each ticket looks like its own. */
function Barcode({ code }: { code: string }) {
  const widths = [...code.replace(/[^0-9A-F]/gi, '')].map((char) => (parseInt(char, 16) % 4) + 1)
  return (
    <div className="ticket__barcode" aria-hidden="true">
      {widths.map((width, index) => (
        <span key={index} style={{ width: `${width * 2}px` }} />
      ))}
    </div>
  )
}
