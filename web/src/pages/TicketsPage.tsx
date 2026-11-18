import { Link } from 'react-router-dom'
import { useMe, useTickets } from '../api/queries'
import { Ticket } from '../components/Ticket'
import './TicketsPage.css'

export function TicketsPage() {
  const { data: tickets, error } = useTickets()
  const signedIn = Boolean(useMe().data?.account)
  const keepHint = signedIn ? null : (
    <p className="muted tickets__keep">
      These are kept in this browser only.{' '}
      <Link to="/sign-in?mode=register&next=/tickets">Create an account</Link> to have them on every device; the ones bought
      here come along.
    </p>
  )

  if (error) {
    return (
      <div className="page-state">
        <h1>Your tickets could not be loaded</h1>
        <p className="muted">Reload the page to try again.</p>
      </div>
    )
  }

  if (!tickets) return null

  if (tickets.length === 0) {
    return (
      <div className="page-state">
        <h1>No tickets yet</h1>
        <p className="muted">
          {signedIn ? 'Tickets you buy with this account show up here, on any device.' : 'Tickets you buy in this browser are kept here.'}
        </p>
        <Link to="/" className="button">
          See what is showing
        </Link>
      </div>
    )
  }

  return (
    <div className="tickets">
      <h1>My tickets</h1>
      {keepHint}
      <ul>
        {tickets.map((ticket) => (
          <li key={ticket.id}>
            <Ticket reservation={ticket} />
          </li>
        ))}
      </ul>
    </div>
  )
}
