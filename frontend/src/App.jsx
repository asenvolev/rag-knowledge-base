import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from './api/client'
import UploadZone from './components/UploadZone'
import DocumentList from './components/DocumentList'
import ChatWindow from './components/ChatWindow'

export default function App() {
  const [documents, setDocuments] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [selectedId, setSelectedId] = useState(null)
  const [sidebarOpen, setSidebarOpen] = useState(false)

  const loadDocuments = useCallback(async () => {
    try {
      setError('')
      setDocuments(await api.listDocuments())
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { loadDocuments() }, [loadDocuments])

  const selectedDocument = useMemo(
    () => documents.find((d) => d.id === selectedId) ?? null,
    [documents, selectedId],
  )

  async function handleDelete(id) {
    try {
      await api.deleteDocument(id)
      if (selectedId === id) setSelectedId(null)
      setDocuments((docs) => docs.filter((d) => d.id !== id))
    } catch (err) {
      setError(err.message)
    }
  }

  function handleUploaded(result) {
    loadDocuments()
    setSelectedId(result.documentId)
    setSidebarOpen(false)
  }

  return (
    <div className="flex h-full flex-col md:flex-row">
      {/* Mobile top bar */}
      <div className="flex items-center justify-between border-b border-slate-200 bg-white px-4 py-3 md:hidden">
        <Brand />
        <button
          onClick={() => setSidebarOpen((o) => !o)}
          className="rounded-lg px-3 py-1.5 text-sm text-slate-700 ring-1 ring-slate-200"
        >
          {sidebarOpen ? 'Close' : `Documents (${documents.length})`}
        </button>
      </div>

      <aside
        className={`${sidebarOpen ? 'flex' : 'hidden'} max-h-[60vh] w-full shrink-0 flex-col gap-5 overflow-y-auto border-b border-slate-200 bg-white p-4 md:flex md:max-h-none md:w-80 md:border-b-0 md:border-r`}
      >
        <div className="hidden md:block"><Brand /></div>
        <UploadZone onUploaded={handleUploaded} />
        <div className="min-h-0">
          <h2 className="mb-2 px-1 text-xs font-semibold uppercase tracking-wide text-slate-500">Library</h2>
          <DocumentList
            documents={documents}
            loading={loading}
            error={error}
            selectedId={selectedId}
            onSelect={(id) => { setSelectedId(id); setSidebarOpen(false) }}
            onDelete={handleDelete}
          />
        </div>
        <p className="mt-auto hidden px-1 text-[11px] text-slate-400 md:block">API: {api.baseUrl || window.location.host}</p>
      </aside>

      <main className="min-h-0 flex-1">
        <ChatWindow
          key={selectedId ?? 'all'}
          scopeDocument={selectedDocument}
          hasDocuments={documents.length > 0}
          onClearScope={() => setSelectedId(null)}
        />
      </main>
    </div>
  )
}

function Brand() {
  return (
    <div className="flex items-center gap-2">
      <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-brand-700 text-sm font-bold text-white">KB</div>
      <div>
        <p className="text-sm font-semibold leading-tight text-slate-900">Knowledge Base</p>
        <p className="text-[11px] leading-tight text-slate-500">Private document Q&amp;A</p>
      </div>
    </div>
  )
}
