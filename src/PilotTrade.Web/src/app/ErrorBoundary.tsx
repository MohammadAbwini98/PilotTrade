import { Component } from 'react'
import type { ReactNode } from 'react'

type Props = { children: ReactNode }

// A page that fails to draw is replaced by a notice; the header, the safety badges and the other pages keep working.
// The error text is not shown, since it could carry data from the reply that failed.
export class ErrorBoundary extends Component<Props, { failed: boolean }> {
  state = { failed: false }

  // Nothing is logged here: a log line could end up in a screenshot or a support bundle.
  static getDerivedStateFromError() {
    return { failed: true }
  }

  render() {
    if (this.state.failed) {
      return (
        <section className="panel" role="alert">
          <h2>This page could not be shown</h2>
          <p>Open another page from the menu, or reload PilotTrade. No order was placed because of this.</p>
        </section>
      )
    }
    return this.props.children
  }
}
