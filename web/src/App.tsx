import { useEffect } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { ApiError, onSessionChange } from './api/client'
import { useCan, useMe } from './api/queries'
import { Layout } from './components/Layout'
import { AccountPage, ChangePassword } from './pages/AccountPage'
import { ActivityPage } from './pages/ActivityPage'
import { CheckoutPage } from './pages/CheckoutPage'
import { HomePage } from './pages/HomePage'
import { MoviePage } from './pages/MoviePage'
import { ProgrammePage } from './pages/ProgrammePage'
import { ShowtimePage } from './pages/ShowtimePage'
import { SignInPage } from './pages/SignInPage'
import { StaffPage } from './pages/StaffPage'
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
  // Signing in or out changes whose tickets, permissions and seat holds every cached answer describes.
  useEffect(() => onSessionChange(() => void queryClient.invalidateQueries()), [])

  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route element={<Layout />}>
            <Route path="*" element={<Pages />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  )
}

function Pages() {
  const me = useMe().data
  const canSchedule = useCan('catalog.schedule')
  const canAudit = useCan('audit.read')
  const canManageStaff = useCan('staff.manage')

  // Someone else chose this password; until the account has its own, nothing else opens.
  if (me?.account?.mustChangePassword) {
    return (
      <div className="page-state">
        <h1>Choose your password</h1>
        <p className="muted">You signed in with a temporary password. Set one only you know to continue.</p>
        <ChangePassword temporary onChanged={() => undefined} />
      </div>
    )
  }

  return (
    <Routes>
      <Route index element={<HomePage />} />
      <Route path="movies/:movieId" element={<MoviePage />} />
      <Route path="showtimes/:showtimeId" element={<ShowtimePage />} />
      <Route path="checkout/:reservationId" element={<CheckoutPage />} />
      <Route path="tickets" element={<TicketsPage />} />
      <Route path="sign-in" element={<SignInPage />} />
      <Route path="account" element={<AccountPage />} />
      {/* Staff screens exist only for the roles that may use them; the API checks again on every call. */}
      {canSchedule && <Route path="programme" element={<ProgrammePage />} />}
      {canAudit && <Route path="activity" element={<ActivityPage />} />}
      {canManageStaff && <Route path="staff" element={<StaffPage />} />}
      {me && !me.account && ['programme', 'activity', 'staff'].map((path) => (
        <Route key={path} path={path} element={<Navigate to={`/sign-in?next=/${path}`} replace />} />
      ))}
      <Route path="*" element={me ? <NotFound /> : null} />
    </Routes>
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
