import { useRef, useState } from 'react'
import { api } from '../api/client'
import { UploadIcon } from './Icons'

const MAX_MB = 20

export default function UploadZone({ onUploaded }) {
  const inputRef = useRef(null)
  const [dragging, setDragging] = useState(false)
  const [state, setState] = useState({ phase: 'idle', progress: 0, message: '' })
  const busy = state.phase === 'uploading' || state.phase === 'processing'

  async function handleFile(file) {
    if (!file || busy) return

    if (file.type !== 'application/pdf' && !file.name.toLowerCase().endsWith('.pdf')) {
      setState({ phase: 'error', progress: 0, message: 'Only PDF files are supported.' })
      return
    }
    if (file.size > MAX_MB * 1024 * 1024) {
      setState({ phase: 'error', progress: 0, message: `File is larger than ${MAX_MB} MB.` })
      return
    }

    setState({ phase: 'uploading', progress: 0, message: file.name })
    try {
      const result = await api.uploadDocument(file, (pct) =>
        setState((s) => ({
          ...s,
          progress: pct,
          // Once bytes are sent, the server is extracting + embedding.
          phase: pct >= 100 ? 'processing' : 'uploading',
        })),
      )
      setState({
        phase: 'done',
        progress: 100,
        message: `${result.fileName} — ${result.pageCount} pages, ${result.chunkCount} chunks indexed`,
      })
      onUploaded?.(result)
    } catch (err) {
      setState({ phase: 'error', progress: 0, message: err.message })
    } finally {
      if (inputRef.current) inputRef.current.value = ''
    }
  }

  function onDrop(e) {
    e.preventDefault()
    setDragging(false)
    handleFile(e.dataTransfer.files?.[0])
  }

  return (
    <div>
      <div
        role="button"
        tabIndex={0}
        aria-disabled={busy}
        onClick={() => !busy && inputRef.current?.click()}
        onKeyDown={(e) => (e.key === 'Enter' || e.key === ' ') && !busy && inputRef.current?.click()}
        onDragOver={(e) => { e.preventDefault(); setDragging(true) }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
        className={[
          'flex flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed px-4 py-6 text-center transition',
          'focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500',
          busy ? 'cursor-wait opacity-70' : 'cursor-pointer',
          dragging ? 'border-brand-500 bg-brand-50' : 'border-slate-300 bg-white hover:border-brand-500 hover:bg-brand-50/40',
        ].join(' ')}
      >
        <div className="flex h-10 w-10 items-center justify-center rounded-full bg-brand-100 text-brand-700">
          <UploadIcon className="h-5 w-5" />
        </div>
        <p className="text-sm font-medium text-slate-800">
          {busy ? 'Working…' : 'Drop a PDF here or click to browse'}
        </p>
        <p className="text-xs text-slate-500">Text-based PDFs, up to {MAX_MB} MB</p>
        <input
          ref={inputRef}
          type="file"
          accept="application/pdf,.pdf"
          className="hidden"
          onChange={(e) => handleFile(e.target.files?.[0])}
        />
      </div>

      {state.phase !== 'idle' && (
        <div className="mt-3 text-xs" aria-live="polite">
          {busy && (
            <>
              <div className="mb-1 flex justify-between text-slate-600">
                <span className="truncate pr-2">{state.message}</span>
                <span>{state.phase === 'processing' ? 'Indexing…' : `${state.progress}%`}</span>
              </div>
              <div className="h-1.5 overflow-hidden rounded-full bg-slate-200">
                <div
                  className={`h-full rounded-full bg-brand-600 transition-all ${state.phase === 'processing' ? 'animate-pulse' : ''}`}
                  style={{ width: `${state.phase === 'processing' ? 100 : state.progress}%` }}
                />
              </div>
            </>
          )}
          {state.phase === 'done' && <p className="rounded-lg bg-emerald-50 px-3 py-2 text-emerald-800">✓ {state.message}</p>}
          {state.phase === 'error' && <p className="rounded-lg bg-red-50 px-3 py-2 text-red-700">{state.message}</p>}
        </div>
      )}
    </div>
  )
}
