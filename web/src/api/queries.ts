import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, postJson } from './client'
import type {
  AuditTrail,
  MovieDetails,
  MovieSummary,
  Payment,
  QueueTicket,
  Reservation,
  Seat,
  ShowtimeDetails,
} from './types'

export function useMovies() {
  return useQuery({ queryKey: ['movies'], queryFn: () => api<MovieSummary[]>('/movies') })
}

export function useMovie(movieId: string) {
  return useQuery({ queryKey: ['movies', movieId], queryFn: () => api<MovieDetails>(`/movies/${movieId}`) })
}

export function useShowtime(showtimeId: string) {
  return useQuery({
    queryKey: ['showtimes', showtimeId],
    queryFn: () => api<ShowtimeDetails>(`/showtimes/${showtimeId}`),
  })
}

/**
 * Entering a showtime joins its waiting room. While waiting, the poll doubles as the heartbeat
 * that keeps the place in line; once admitted there is nothing left to poll for.
 */
export function useWaitingRoom(showtimeId: string) {
  const join = useQuery({
    queryKey: ['queue', showtimeId, 'join'],
    queryFn: () => postJson<QueueTicket>(`/showtimes/${showtimeId}/queue`),
    staleTime: Infinity,
    gcTime: 0,
  })

  const waiting = join.data?.state === 'waiting'
  const poll = useQuery({
    queryKey: ['queue', showtimeId, 'status'],
    queryFn: () => api<QueueTicket>(`/showtimes/${showtimeId}/queue`),
    enabled: waiting,
    refetchInterval: (query) => (query.state.data?.state === 'admitted' ? false : 2000),
    gcTime: 0,
  })

  return { ticket: poll.data ?? join.data, error: join.error ?? poll.error, rejoin: join.refetch }
}

export function useSeats(showtimeId: string, enabled: boolean) {
  return useQuery({
    queryKey: ['seats', showtimeId],
    queryFn: () => api<Seat[]>(`/showtimes/${showtimeId}/seats`),
    enabled,
    // Other customers are choosing at the same time; keep the map close to reality.
    refetchInterval: 3000,
  })
}

export function useHoldSeats(showtimeId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (seats: string[]) => postJson<Reservation>(`/showtimes/${showtimeId}/reservations`, { seats }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['seats', showtimeId] }),
  })
}

export function useReservation(reservationId: string, refetchInterval: number | false) {
  return useQuery({
    queryKey: ['reservations', reservationId],
    queryFn: () => api<Reservation>(`/reservations/${reservationId}`),
    refetchInterval,
  })
}

export function useCancelReservation() {
  return useMutation({
    mutationFn: (reservationId: string) => api<void>(`/reservations/${reservationId}`, { method: 'DELETE' }),
  })
}

export function usePay(reservationId: string) {
  return useMutation({
    mutationFn: ({ paymentToken, idempotencyKey }: { paymentToken: string; idempotencyKey: string }) =>
      postJson<Payment>(`/reservations/${reservationId}/payments`, { paymentToken }, {
        'Idempotency-Key': idempotencyKey,
      }),
  })
}

export function usePayment(paymentId: string | undefined) {
  return useQuery({
    queryKey: ['payments', paymentId],
    queryFn: () => api<Payment>(`/payments/${paymentId}`),
    enabled: paymentId !== undefined,
    refetchInterval: (query) => (query.state.data?.status === 'pending' || !query.state.data ? 1000 : false),
  })
}

export function useTickets() {
  return useQuery({ queryKey: ['reservations'], queryFn: () => api<Reservation[]>('/reservations') })
}

export function useAuditTrail() {
  return useQuery({
    queryKey: ['audit'],
    queryFn: () => api<AuditTrail>('/audit/changes?take=60'),
    refetchInterval: 3000,
  })
}
