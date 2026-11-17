import { type FormEvent, useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import QRCode from 'qrcode'
import { Navigate } from 'react-router-dom'
import { type Session, api, postJson, setSession } from '../api/client'
import { useMe } from '../api/queries'
import type { DeviceSession } from '../api/types'
import { Dialog } from '../components/Dialog'
import { describeDevice } from '../lib/devices'
import { formatDateTime } from '../lib/format'
import './AccountPage.css'

const ROLE_NAMES: Record<string, string> = {
  customer: 'Customer',
  programmer: 'Programmer',
  auditor: 'Auditor',
  admin: 'Administrator',
}

/** What every account manages for itself: password, two-step sign-in, signed-in devices. */
export function AccountPage() {
  const { data: me } = useMe()
  const queryClient = useQueryClient()
  const [dialog, setDialog] = useState<'two-step' | 'disable'>()
  const [notice, setNotice] = useState<string>()

  if (me && !me.account) return <Navigate to="/sign-in?next=/account" replace />
  if (!me?.account) return null
  const account = me.account
  const refreshMe = () => queryClient.invalidateQueries({ queryKey: ['me'] })

  return (
    <div className="account">
      <header>
        <h1>Your account</h1>
        <p className="muted">
          {account.fullName}, {account.email}. {ROLE_NAMES[account.role]}.
        </p>
      </header>

      {notice && (
        <p className="notice account__notice" role="status">
          {notice}
        </p>
      )}

      <section className="panel">
        <h2>Password</h2>
        <p className="muted">Changing it signs you out on every other device.</p>
        <ChangePassword onChanged={() => setNotice('Password changed. Your other devices have been signed out.')} />
      </section>

      <section className="panel account__row">
        <div>
          <h2>
            Two-step sign-in{' '}
            {account.twoStepEnabled ? <span className="tag tag--ok">On</span> : <span className="tag">Off</span>}
          </h2>
          <p className="muted">
            Besides the password, a code from an authenticator app on your phone. A stolen password alone then gets nobody in.
          </p>
        </div>
        <button
          type="button"
          className={account.twoStepEnabled ? 'button button--quiet' : 'button'}
          onClick={() => setDialog(account.twoStepEnabled ? 'disable' : 'two-step')}
        >
          {account.twoStepEnabled ? 'Turn off' : 'Turn on'}
        </button>
      </section>

      <Sessions />

      {dialog === 'two-step' && <TwoStepSetup onClose={() => setDialog(undefined)} onEnabled={() => void refreshMe()} />}
      {dialog === 'disable' && (
        <DisableTwoStep
          onClose={() => setDialog(undefined)}
          onDisabled={() => {
            setDialog(undefined)
            setNotice('Two-step sign-in is off.')
            void refreshMe()
          }}
        />
      )}
    </div>
  )
}

/** Also the whole page for a staff member still on the temporary password an administrator gave them. */
export function ChangePassword({ onChanged, temporary = false }: { onChanged: () => void; temporary?: boolean }) {
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const change = useMutation({
    mutationFn: () => postJson<Session>('/account/password', { currentPassword: current, newPassword: next }),
    onSuccess: (session) => {
      setSession(session)
      setCurrent('')
      setNext('')
      onChanged()
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    change.mutate()
  }

  return (
    <form className="form" onSubmit={submit}>
      <label className="field">
        {temporary ? 'Temporary password' : 'Current password'}
        <input type="password" autoComplete="current-password" required value={current} onChange={(e) => setCurrent(e.target.value)} />
      </label>
      <label className="field">
        New password <small>At least 12 characters. A short sentence works well.</small>
        <input type="password" autoComplete="new-password" minLength={12} required value={next} onChange={(e) => setNext(e.target.value)} />
      </label>
      {change.error && (
        <p className="form-error" role="alert">
          {change.error.message}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button" disabled={change.isPending}>
          Save the new password
        </button>
      </div>
    </form>
  )
}

function TwoStepSetup({ onClose, onEnabled }: { onClose: () => void; onEnabled: () => void }) {
  const [qr, setQr] = useState<string>()
  const [code, setCode] = useState('')
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>()
  const setup = useQuery({
    queryKey: ['account', 'two-step-setup'],
    queryFn: () => postJson<{ secret: string; setupUri: string }>('/account/two-step/setup'),
    staleTime: Infinity,
    gcTime: 0,
  })

  useEffect(() => {
    // Drawn in the browser: the secret never goes to a QR-code service.
    if (setup.data) void QRCode.toDataURL(setup.data.setupUri, { margin: 1, width: 200 }).then(setQr)
  }, [setup.data])

  const enable = useMutation({
    mutationFn: () => postJson<{ codes: string[] }>('/account/two-step/enable', { code }),
    onSuccess: ({ codes }) => {
      setRecoveryCodes(codes)
      onEnabled()
    },
  })

  if (recoveryCodes) {
    return (
      <Dialog title="Your recovery codes" onClose={onClose}>
        <p className="muted">
          Each one signs you in once when your phone is not at hand. They will not be shown again: print them or keep them in
          a password manager.
        </p>
        <ul className="recovery-codes">
          {recoveryCodes.map((recovery) => (
            <li key={recovery}>{recovery}</li>
          ))}
        </ul>
        <button type="button" className="button" onClick={onClose}>
          I have saved them
        </button>
      </Dialog>
    )
  }

  return (
    <Dialog title="Turn on two-step sign-in" onClose={onClose}>
      <ol className="two-step">
        <li>
          Scan this with your authenticator app.
          <div className="two-step__qr">{qr && <img src={qr} alt="QR code for the authenticator app" width={200} height={200} />}</div>
          {setup.data && (
            <p className="muted">
              Or type the key: <code className="two-step__secret">{setup.data.secret}</code>
            </p>
          )}
        </li>
        <li>
          <label className="field">
            The six-digit code the app shows
            <input inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(e.target.value)} />
          </label>
        </li>
      </ol>
      {(setup.error ?? enable.error) && <p className="form-error">{(setup.error ?? enable.error)?.message}</p>}
      <div className="form-actions">
        <button type="button" className="button" disabled={code.length !== 6 || enable.isPending} onClick={() => enable.mutate()}>
          Verify and turn on
        </button>
        <button type="button" className="button button--quiet" onClick={onClose}>
          Cancel
        </button>
      </div>
    </Dialog>
  )
}

function DisableTwoStep({ onClose, onDisabled }: { onClose: () => void; onDisabled: () => void }) {
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const disable = useMutation({ mutationFn: () => postJson('/account/two-step/disable', { password, code }), onSuccess: onDisabled })

  return (
    <Dialog title="Turn off two-step sign-in" onClose={onClose}>
      <form
        className="form"
        onSubmit={(event) => {
          event.preventDefault()
          disable.mutate()
        }}
      >
        <p className="muted">Both are needed: your password and a current code (or a recovery code).</p>
        <label className="field">
          Password
          <input type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        <label className="field">
          Code
          <input autoComplete="one-time-code" required value={code} onChange={(e) => setCode(e.target.value)} />
        </label>
        {disable.error && <p className="form-error">{disable.error.message}</p>}
        <div className="form-actions">
          <button type="submit" className="button" disabled={disable.isPending}>
            Turn off
          </button>
        </div>
      </form>
    </Dialog>
  )
}

function Sessions() {
  const queryClient = useQueryClient()
  const { data: sessions } = useQuery({ queryKey: ['account', 'sessions'], queryFn: () => api<DeviceSession[]>('/account/sessions') })
  const end = useMutation({
    mutationFn: (id: string) => api(`/account/sessions/${id}`, { method: 'DELETE' }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['account', 'sessions'] }),
  })

  return (
    <section className="panel">
      <h2>Signed-in devices</h2>
      <p className="muted">If you do not recognise one, sign it out and change your password.</p>
      <ul className="devices">
        {sessions?.map((session) => (
          <li key={session.id}>
            <div>
              <strong>{describeDevice(session.device)}</strong>
              <small className="muted">
                Signed in {formatDateTime(session.startedAt)}, last used {formatDateTime(session.lastUsedAt)}
              </small>
            </div>
            {session.isCurrent ? (
              <span className="tag tag--ok">This device</span>
            ) : (
              <button type="button" className="button button--quiet" disabled={end.isPending} onClick={() => end.mutate(session.id)}>
                Sign out
              </button>
            )}
          </li>
        ))}
      </ul>
    </section>
  )
}
