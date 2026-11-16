import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { ApiError } from './api/client'
import { Layout } from './components/Layout'
import { ActivityPage } from './pages/ActivityPage'
import { CheckoutPage } from './pages/CheckoutPage'
import { HomePage } from './pages/HomePage'
import { MoviePage } from './pages/MoviePage'
import { ShowtimePage } from './pages/ShowtimePage'
import { TicketsPage } from './pages/TicketsPage'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // An answer from the API (not found, conflict, ...) will not change by asking again.
      retry: (failures, error) => !(error instanceof ApiError && error.status < 500) && failures < 2,
      refetchOnWindowFocus: false,
    },
  },
})

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<HomePage />} />
            <Route path="movies/:movieId" element={<MoviePage />} />
            <Route path="showtimes/:showtimeId" element={<ShowtimePage />} />
            <Route path="checkout/:reservationId" element={<CheckoutPage />} />
            <Route path="tickets" element={<TicketsPage />} />
            <Route path="activity" element={<ActivityPage />} />
            <Route path="*" element={<NotFound />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  )
}

function NotFound() {
  return (
    <div className="page-state">
      <h1>There is nothing at this address</h1>
      <a href="/" className="button">
        See what is showing
      </a>
    </div>
  )
}
