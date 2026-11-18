import { type FormEvent, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, postJson, putJson } from '../api/client'
import { useMe } from '../api/queries'
import type { StaffMember } from '../api/types'
import { Dialog } from '../components/Dialog'
import { formatDateTime } from '../lib/format'
import './StaffPage.css'

const ROLES: { role: StaffMember['role']; name: string; what: string }[] = [
  { role: 'programmer', name: 'Programmer', what: 'Plans the programme: which film plays in which hall, when.' },
  { role: 'auditor', name: 'Auditor', what: 'Reads the audit trail of every booking and payment change. Changes nothing.' },
  { role: 'admin', name: 'Administrator', what: 'Everything above, plus these staff accounts.' },
]

type Action = { kind: 'add' } | { kind: 'edit'; member: StaffMember } | { kind: 'password'; member: StaffMember; password: string }

/** The cinema's staff. Administrators only; the API refuses everyone else. */
export function StaffPage() {
  const queryClient = useQueryClient()
  const me = useMe().data
  const [action, setAction] = useState<Action>()
  const { data: staff } = useQuery({ queryKey: ['staff'], queryFn: () => api<StaffMember[]>('/staff') })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['staff'] })
  const reset = useMutation({
    mutationFn: (member: StaffMember) => postJson<{ member: StaffMember; password: string }>(`/staff/${member.id}/password-reset`),
    onSuccess: ({ member, password }) => {
      setAction({ kind: 'password', member, password })
      void refresh()
    },
  })

  return (
    <div className="staff">
      <header className="staff__header">
        <div>
          <h1>Staff</h1>
          <p className="muted">Everyone signs in with their own account; the role decides which screens open.</p>
        </div>
        <button type="button" className="button" onClick={() => setAction({ kind: 'add' })}>
          Add someone
        </button>
      </header>

      <dl className="staff__roles">
        {ROLES.map(({ role, name, what }) => (
          <div key={role}>
            <dt>{name}</dt>
            <dd className="muted">{what}</dd>
          </div>
        ))}
      </dl>

      {reset.error && <p className="form-error">{reset.error.message}</p>}

      <ul className="staff__list">
        {staff?.map((member) => (
          <li key={member.id} className={member.isActive ? undefined : 'staff__member--inactive'}>
            <div>
              <strong>
                {member.fullName}
                {member.id === me?.account?.id && <span className="muted"> (you)</span>}
              </strong>
              <small className="muted">{member.email}</small>
            </div>
            <div className="staff__tags">
              <span>{ROLES.find((r) => r.role === member.role)?.name}</span>
              {!member.isActive && <span className="tag">Deactivated</span>}
              {member.isActive && member.mustChangePassword && <span className="tag tag--warn">Has not set a password</span>}
              {member.twoStepEnabled && <span className="tag tag--ok">Two-step</span>}
            </div>
            <small className="muted">{member.lastSignInAt ? `Last in ${formatDateTime(member.lastSignInAt)}` : 'Never signed in'}</small>
            {member.id !== me?.account?.id ? (
              <div className="staff__actions">
                <button type="button" className="button button--quiet" onClick={() => setAction({ kind: 'edit', member })}>
                  Edit
                </button>
                <button
                  type="button"
                  className="button button--quiet"
                  disabled={!member.isActive || reset.isPending}
                  onClick={() => reset.mutate(member)}
                >
                  Reset password
                </button>
              </div>
            ) : (
              <span />
            )}
          </li>
        ))}
      </ul>

      {action?.kind === 'add' && (
        <MemberForm
          title="Add someone"
          onClose={() => setAction(undefined)}
          onSubmit={(body) =>
            postJson<{ member: StaffMember; password: string }>('/staff', body).then(({ member, password }) => {
              setAction({ kind: 'password', member, password })
              void refresh()
            })
          }
        />
      )}

      {action?.kind === 'edit' && (
        <MemberForm
          title={`Edit ${action.member.fullName}`}
          member={action.member}
          onClose={() => setAction(undefined)}
          onSubmit={(body) =>
            putJson(`/staff/${action.member.id}`, body).then(() => {
              setAction(undefined)
              void refresh()
            })
          }
        />
      )}

      {action?.kind === 'password' && (
        <Dialog title="Temporary password" onClose={() => setAction(undefined)}>
          <p className="muted">
            {action.member.fullName} signs in with this once and then has to choose a password of their own. Pass it on in person
            or over a channel you trust; it will not be shown again.
          </p>
          <p className="secret staff__secret">{action.password}</p>
          <button type="button" className="button" onClick={() => setAction(undefined)}>
            Done
          </button>
        </Dialog>
      )}
    </div>
  )
}

interface MemberFormProps {
  title: string
  member?: StaffMember
  onClose: () => void
  onSubmit: (body: { fullName: string; email?: string; role: string; isActive?: boolean }) => Promise<unknown>
}

function MemberForm({ title, member, onClose, onSubmit }: MemberFormProps) {
  const [fullName, setFullName] = useState(member?.fullName ?? '')
  const [email, setEmail] = useState('')
  const [role, setRole] = useState<string>(member?.role ?? 'programmer')
  const [isActive, setIsActive] = useState(member?.isActive ?? true)
  const save = useMutation({
    mutationFn: () => onSubmit(member ? { fullName, role, isActive } : { fullName, email, role }),
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    save.mutate()
  }

  return (
    <Dialog title={title} onClose={onClose}>
      <form className="form" onSubmit={submit}>
        <label className="field">
          Name
          <input required value={fullName} onChange={(e) => setFullName(e.target.value)} />
        </label>
        {!member && (
          <label className="field">
            E-mail <small>A temporary password is generated; they choose their own at the first sign-in.</small>
            <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          </label>
        )}
        <fieldset className="staff__role-picker">
          <legend>Role</legend>
          {ROLES.map((option) => (
            <label key={option.role} className="check">
              <input type="radio" name="role" checked={role === option.role} onChange={() => setRole(option.role)} />
              <span>
                <strong>{option.name}.</strong> {option.what}
              </span>
            </label>
          ))}
        </fieldset>
        {member && (
          <label className="check">
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
            <span>Active. Deactivating signs them out everywhere at once.</span>
          </label>
        )}
        {member && <p className="muted">A new role also signs them out everywhere; they sign in again with the new one.</p>}
        {save.error && <p className="form-error">{save.error.message}</p>}
        <div className="form-actions">
          <button type="submit" className="button" disabled={save.isPending}>
            Save
          </button>
          <button type="button" className="button button--quiet" onClick={onClose}>
            Cancel
          </button>
        </div>
      </form>
    </Dialog>
  )
}
