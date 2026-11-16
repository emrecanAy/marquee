import { Link } from 'react-router-dom'
import { useTickets } from '../api/queries'
import { Ticket } from '../components/Ticket'
import './TicketsPage.css'

export function TicketsPage() {
  const { data: tickets, error } = useTickets()

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
        <p className="muted">Tickets you buy in this browser are kept here.</p>
        <Link to="/" className="button">
          See what is showing
        </Link>
      </div>
    )
  }

  return (
    <div className="tickets">
      <h1>My tickets</h1>
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
