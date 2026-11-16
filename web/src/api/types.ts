export interface MovieSummary {
  id: string
  title: string
  tagline: string
  genre: string
  durationMinutes: number
  ageRating: string
  posterHue: number
  nextShowtime: string | null
}

export interface ShowtimeSlot {
  id: string
  startsAt: string
  hallName: string
  priceFrom: number
}

export interface MovieDetails extends Omit<MovieSummary, 'nextShowtime'> {
  synopsis: string
  director: string
  showtimes: ShowtimeSlot[]
}

export interface ShowtimeDetails {
  id: string
  startsAt: string
  movieId: string
  movieTitle: string
  posterHue: number
  durationMinutes: number
  hallName: string
  rowCount: number
  seatsPerRow: number
}

export type SeatStatus = 'available' | 'held' | 'sold' | 'mine'

export interface Seat {
  label: string
  row: string
  number: number
  category: 'standard' | 'premium'
  price: number
  status: SeatStatus
}

export interface QueueTicket {
  state: 'none' | 'waiting' | 'admitted'
  position: number
  admittedUntil: string | null
}

export type ReservationStatus = 'pending' | 'confirmed' | 'cancelled' | 'expired'

export interface Reservation {
  id: string
  showtimeId: string
  movieTitle: string
  hallName: string
  startsAt: string
  seats: string[]
  amount: number
  status: ReservationStatus
  expiresAt: string
  ticketCode: string | null
}

export interface Payment {
  id: string
  reservationId: string
  amount: number
  status: 'pending' | 'succeeded' | 'failed' | 'refunded'
  failureReason: string | null
}

export interface ChangeEvent {
  id: number
  table: string
  operation: 'insert' | 'update' | 'delete' | 'snapshot'
  before: Record<string, unknown> | null
  after: Record<string, unknown> | null
  committedAt: string
}

export interface AuditTrail {
  enabled: boolean
  changes: ChangeEvent[]
}
