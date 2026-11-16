import { type FormEvent, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useCancelReservation, usePay, usePayment, useReservation } from '../api/queries'
import type { Reservation } from '../api/types'
import { Ticket } from '../components/Ticket'
import { formatCardNumber, TEST_CARDS, tokenizeCard } from '../lib/cards'
import { formatCountdown, formatMoney } from '../lib/format'
import { useNow } from '../lib/useNow'
import './CheckoutPage.css'

export function CheckoutPage() {
  const { reservationId = '' } = useParams()
  const [paymentId, setPaymentId] = useState<string>()
  const payment = usePayment(paymentId).data
  const charging = paymentId !== undefined && (payment === undefined || payment.status !== 'failed')
  // While a charge is in flight the confirmation can arrive any second. Otherwise a slow poll is
  // enough to notice the hold running out.
  const reservation = useReservation(reservationId, charging ? 1000 : 5000)

  if (reservation.error) {
    return (
      <div className="page-state">
        <h1>This order does not exist</h1>
        <p className="muted">It may belong to another browser, or the link is incomplete.</p>
        <Link to="/" className="button">
          See what is showing
        </Link>
      </div>
    )
  }

  if (!reservation.data) return null
  const order = reservation.data

  if (order.status === 'confirmed') {
    return (
      <div className="checkout checkout--done">
        <h1>Enjoy the film</h1>
        <p className="checkout__lead">Show this ticket at the door. It is also saved under My tickets in this browser.</p>
        <Ticket reservation={order} />
        <Link to="/" className="button button--quiet">
          Back to the programme
        </Link>
      </div>
    )
  }

  if (order.status !== 'pending') {
    return (
      <div className="page-state">
        <h1>{order.status === 'expired' ? 'The hold on your seats ran out' : 'This order was cancelled'}</h1>
        <p className="muted">
          {payment?.status === 'refunded'
            ? 'Your payment arrived after the seats were released, so it has been refunded in full.'
            : 'The seats are back on sale. Nothing was charged.'}
        </p>
        <Link to={`/showtimes/${order.showtimeId}`} className="button">
          Choose seats again
        </Link>
      </div>
    )
  }

  return (
    <PendingOrder
      order={order}
      charging={charging}
      declined={payment?.status === 'failed'}
      onPaymentStarted={setPaymentId}
    />
  )
}

interface PendingOrderProps {
  order: Reservation
  charging: boolean
  declined: boolean
  onPaymentStarted: (paymentId: string) => void
}

function PendingOrder({ order, charging, declined, onPaymentStarted }: PendingOrderProps) {
  const navigate = useNavigate()
  const now = useNow()
  const pay = usePay(order.id)
  const cancel = useCancelReservation()
  const [cardNumber, setCardNumber] = useState('')
  const [cardError, setCardError] = useState<string>()
  // One key per payment attempt. If the request is repeated (double click, flaky network), the
  // server recognises the key and returns the first payment instead of charging twice.
  const idempotencyKey = useRef(crypto.randomUUID())

  const busy = charging || pay.isPending

  function submit(event: FormEvent) {
    event.preventDefault()
    const card = tokenizeCard(cardNumber)
    if (!card.ok) {
      setCardError(card.reason)
      return
    }

    setCardError(undefined)
    if (declined) idempotencyKey.current = crypto.randomUUID()
    pay.mutate(
      { paymentToken: card.token, idempotencyKey: idempotencyKey.current },
      { onSuccess: (payment) => onPaymentStarted(payment.id) },
    )
  }

  function release() {
    cancel.mutate(order.id, { onSuccess: () => navigate(`/showtimes/${order.showtimeId}`) })
  }

  const problem = cardError ?? pay.error?.message ?? (declined && !busy ? 'The card was declined. Try another card.' : undefined)

  return (
    <div className="checkout">
      <div className="checkout__order">
        <h1>Your seats are held</h1>
        <p className="checkout__lead">
          Pay within <strong className="checkout__countdown">{formatCountdown(order.expiresAt, now)}</strong> to keep them.
        </p>
        <Ticket reservation={order} />
        <button type="button" className="button button--quiet" disabled={busy || cancel.isPending} onClick={release}>
          Release seats and choose again
        </button>
      </div>

      <form className="checkout__payment" onSubmit={submit} noValidate>
        <h2>Payment</h2>

        <label className="field">
          <span>Card number</span>
          <input
            inputMode="numeric"
            autoComplete="cc-number"
            placeholder="0000 0000 0000 0000"
            value={cardNumber}
            disabled={busy}
            aria-invalid={cardError !== undefined}
            onChange={(event) => setCardNumber(formatCardNumber(event.target.value))}
          />
        </label>
        <div className="field-row">
          <label className="field">
            <span>Expiry</span>
            <input autoComplete="cc-exp" placeholder="MM / YY" defaultValue="12 / 30" disabled={busy} />
          </label>
          <label className="field">
            <span>Security code</span>
            <input inputMode="numeric" autoComplete="cc-csc" placeholder="CVC" defaultValue="123" disabled={busy} />
          </label>
        </div>

        {problem && (
          <p className="notice" role="alert">
            {problem}
          </p>
        )}

        <button type="submit" className="button" disabled={busy}>
          {busy ? 'Contacting your bank' : `Pay ${formatMoney(order.amount)}`}
        </button>

        <div className="checkout__sandbox">
          <p>This is a sandbox. Pick a test card to see each outcome:</p>
          <ul>
            {TEST_CARDS.map((card) => (
              <li key={card.token}>
                <button type="button" disabled={busy} onClick={() => setCardNumber(card.number)}>
                  {card.number}
                </button>
                <span>{card.outcome}</span>
              </li>
            ))}
          </ul>
        </div>
      </form>
    </div>
  )
}
