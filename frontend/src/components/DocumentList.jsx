import { FileIcon, TrashIcon } from './Icons'

export default function DocumentList({ documents, loading, error, selectedId, onSelect, onDelete }) {
  if (loading) {
    return (
      <div className="space-y-2">
        {[0, 1, 2].map((i) => <div key={i} className="h-12 animate-pulse rounded-lg bg-slate-200/70" />)}
      </div>
    )
  }

  if (error) return <p className="rounded-lg bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>

  if (documents.length === 0) {
    return <p className="px-1 text-xs text-slate-500">No documents yet. Upload a PDF to get started.</p>
  }

  return (
    <ul className="space-y-1">
      <li>
        <button
          onClick={() => onSelect(null)}
          className={`w-full rounded-lg px-3 py-2 text-left text-sm transition ${
            selectedId === null ? 'bg-brand-600 text-white' : 'text-slate-700 hover:bg-slate-100'
          }`}
        >
          All documents <span className="opacity-70">({documents.length})</span>
        </button>
      </li>
      {documents.map((doc) => {
        const active = selectedId === doc.id
        return (
          <li key={doc.id} className="group relative">
            <button
              onClick={() => onSelect(doc.id)}
              title={doc.fileName}
              className={`flex w-full items-start gap-2 rounded-lg px-3 py-2 pr-9 text-left transition ${
                active ? 'bg-brand-50 ring-1 ring-brand-500' : 'hover:bg-slate-100'
              }`}
            >
              <FileIcon className={`mt-0.5 h-4 w-4 shrink-0 ${active ? 'text-brand-700' : 'text-slate-400'}`} />
              <span className="min-w-0">
                <span className="block truncate text-sm font-medium text-slate-800">{doc.fileName}</span>
                <span className="block text-xs text-slate-500">
                  {doc.pageCount} pages · {doc.chunkCount} chunks · {(doc.fileSizeBytes / 1024 / 1024).toFixed(1)} MB
                </span>
              </span>
            </button>
            <button
              onClick={() => {
                if (confirm(`Delete "${doc.fileName}" and its embeddings?`)) onDelete(doc.id)
              }}
              aria-label={`Delete ${doc.fileName}`}
              className="absolute right-2 top-2 rounded-md p-1 text-slate-400 opacity-100 transition hover:bg-red-50 hover:text-red-600 md:opacity-0 md:group-hover:opacity-100 focus:opacity-100"
            >
              <TrashIcon className="h-4 w-4" />
            </button>
          </li>
        )
      })}
    </ul>
  )
}
