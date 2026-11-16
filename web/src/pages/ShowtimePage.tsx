import { Link, useNavigate, useParams } from 'react-router-dom'
import { useSeats, useShowtime, useWaitingRoom } from '../api/queries'
import { SeatPicker } from '../components/SeatPicker'
import { formatDay, formatTime } from '../lib/format'
import './ShowtimePage.css'

export function ShowtimePage() {
  const { showtimeId = '' } = useParams()
  const navigate = useNavigate()
  const showtime = useShowtime(showtimeId)
  const waitingRoom = useWaitingRoom(showtimeId)
  const admitted = waitingRoom.ticket?.state === 'admitted'
  const seats = useSeats(showtimeId, admitted)

  if (showtime.error) {
    return (
      <div className="page-state">
        <h1>This showtime does not exist</h1>
        <Link to="/" className="button">
          See what is showing
        </Link>
      </div>
    )
  }

  if (!showtime.data) return null

  return (
    <div className="showtime">
      <header className="showtime__header">
        <Link to={`/movies/${showtime.data.movieId}`} className="showtime__back">
          All showtimes for this film
        </Link>
        <h1>{showtime.data.movieTitle}</h1>
        <p className="showtime__when">
          {formatDay(showtime.data.startsAt)} at {formatTime(showtime.data.startsAt)}, {showtime.data.hallName}
        </p>
      </header>

      {waitingRoom.error ? (
        <p className="notice">The waiting room could not be reached. Reload the page to try again.</p>
      ) : waitingRoom.ticket?.state === 'waiting' ? (
        <WaitingRoom position={waitingRoom.ticket.position} />
      ) : admitted && seats.data ? (
        seats.data.length === 0 ? (
          <p className="notice">Seats for this showtime are still being set up. This takes a few seconds.</p>
        ) : (
          <SeatPicker
            showtimeId={showtimeId}
            seats={seats.data}
            onHeld={(reservation) => navigate(`/checkout/${reservation.id}`)}
            onAdmissionLost={() => void waitingRoom.rejoin()}
          />
        )
      ) : null}
    </div>
  )
}

function WaitingRoom({ position }: { position: number }) {
  return (
    <section className="queue" aria-live="polite">
      <p className="queue__label">Your place in line</p>
      <p className="queue__position">{position}</p>
      <p className="queue__copy">
        Many people are choosing seats for this showing right now. You will be let in automatically, in the order
        everyone arrived. Keep this tab open: closing it gives up your place.
      </p>
    </section>
  )
}
