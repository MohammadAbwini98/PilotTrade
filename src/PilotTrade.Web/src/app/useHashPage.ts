import { useEffect, useState } from 'react'
import { pageFromHash } from './pages'
import type { PageId } from './pages'

// The page lives in the address (#/markets), so Back, Forward and a reload all land where the owner was.
export function useHashPage() {
  const [page, setPage] = useState<PageId>(() => pageFromHash(window.location.hash))

  useEffect(() => {
    const sync = () => setPage(pageFromHash(window.location.hash))
    window.addEventListener('popstate', sync)
    window.addEventListener('hashchange', sync)
    return () => {
      window.removeEventListener('popstate', sync)
      window.removeEventListener('hashchange', sync)
    }
  }, [])

  const go = (id: PageId) => {
    if (id === page) return
    window.history.pushState(null, '', `#/${id}`)
    setPage(id)
  }

  return [page, go] as const
}
