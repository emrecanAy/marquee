import { useAuditTrail } from '../api/queries'
import type { ChangeEvent } from '../api/types'
import './ActivityPage.css'

const time = new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit' })

/**
 * A window onto the audit trail: every row change in the booking and payments tables, as captured
 * from the PostgreSQL write-ahead log by Debezium. Useful for watching what a purchase really does.
 */
export function ActivityPage() {
  const { data, error } = useAuditTrail()

  if (error) {
    return (
      <div className="page-state">
        <h1>The audit trail could not be loaded</h1>
        <p className="muted">Reload the page to try again.</p>
      </div>
    )
  }

  if (!data) return null

  return (
    <div className="activity">
      <h1>Database activity</h1>
      <p className="activity__lead">
        Every change to reservations, seats and payments, read from the PostgreSQL write-ahead log by Debezium. The
        application writes none of these records itself.
      </p>

      {!data.enabled ? (
        <div className="notice">
          <p>Change data capture is not running. Start the stack with it:</p>
          <pre>docker compose -f compose.yaml -f compose.cdc.yaml up --build</pre>
        </div>
      ) : data.changes.length === 0 ? (
        <p className="muted">Nothing has changed yet. Hold a seat in another tab and watch this page.</p>
      ) : (
        <div className="activity__scroll">
          <table>
            <thead>
              <tr>
                <th scope="col">Committed</th>
                <th scope="col">Table</th>
                <th scope="col">Change</th>
                <th scope="col">What changed</th>
              </tr>
            </thead>
            <tbody>
              {data.changes.map((change) => (
                <tr key={change.id}>
                  <td>{time.format(new Date(change.committedAt))}</td>
                  <td>{change.table}</td>
                  <td>
                    <span className={`activity__op activity__op--${change.operation}`}>{change.operation}</span>
                  </td>
                  <td>{summarise(change)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

/** For updates, only the columns whose value differs; otherwise the identifying columns of the row. */
function summarise(change: ChangeEvent): string {
  const row = change.after ?? change.before ?? {}
  if (change.operation === 'update' && change.before && change.after) {
    const before = change.before
    const changed = Object.keys(change.after).filter(
      (column) => JSON.stringify(before[column]) !== JSON.stringify(row[column]),
    )
    if (changed.length > 0) {
      const subject = typeof row.label === 'string' ? `Seat ${row.label}, ` : ''
      return subject + changed.map((column) => `${column}: ${show(before[column])} to ${show(row[column])}`).join(', ')
    }
  }

  return ['label', 'status', 'amount', 'ticket_code']
    .filter((column) => row[column] != null)
    .map((column) => `${column}: ${show(row[column])}`)
    .join(', ')
}

function show(value: unknown): string {
  if (value == null) return 'empty'
  const text = typeof value === 'string' ? value : JSON.stringify(value)
  return text.length > 28 ? `${text.slice(0, 25)}...` : text
}
