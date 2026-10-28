import { useState } from 'react'
import { SparkIcon } from './Icons'

/** Renders **bold** and [n] citation markers without pulling in a markdown library. */
function RichText({ text }) {
  const parts = text.split(/(\*\*[^*]+\*\*|\[\d+\])/g)
  return parts.map((part, i) => {
    if (/^\*\*[^*]+\*\*$/.test(part)) return <strong key={i}>{part.slice(2, -2)}</strong>
    if (/^\[\d+\]$/.test(part)) {
      return (
        <sup key={i} className="mx-0.5 rounded bg-brand-100 px-1 text-[10px] font-semibold text-brand-700">
          {part.slice(1, -1)}
        </sup>
      )
    }
    return <span key={i}>{part}</span>
  })
}

function Sources({ sources }) {
  const [open, setOpen] = useState(false)
  if (!sources?.length) return null

  return (
    <div className="mt-3 border-t border-slate-100 pt-2">
      <button
        onClick={() => setOpen((o) => !o)}
        className="text-xs font-medium text-brand-700 hover:underline"
        aria-expanded={open}
      >
        {open ? 'Hide' : 'Show'} {sources.length} source{sources.length > 1 ? 's' : ''}
      </button>
      {open && (
        <ol className="mt-2 space-y-2">
          {sources.map((s, i) => (
            <li key={`${s.documentId}-${i}`} className="rounded-lg bg-slate-50 p-2 text-xs">
              <div className="mb-1 flex items-center justify-between gap-2">
                <span className="truncate font-medium text-slate-700">
                  [{i + 1}] {s.fileName} · p. {s.pageNumber}
                </span>
                <span className="shrink-0 rounded bg-white px-1.5 py-0.5 text-[10px] text-slate-500 ring-1 ring-slate-200">
                  {(s.similarity * 100).toFixed(0)}% match
                </span>
              </div>
              <p className="text-slate-600">{s.snippet}</p>
            </li>
          ))}
        </ol>
      )}
    </div>
  )
}

export default function MessageBubble({ message }) {
  const isUser = message.role === 'user'

  if (isUser) {
    return (
      <div className="flex justify-end">
        <div className="max-w-[85%] whitespace-pre-wrap rounded-2xl rounded-br-md bg-brand-600 px-4 py-2.5 text-sm text-white shadow-sm">
          {message.content}
        </div>
      </div>
    )
  }

  return (
    <div className="flex gap-3">
      <div className="mt-1 flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-brand-100 text-brand-700">
        <SparkIcon className="h-4 w-4" />
      </div>
      <div
        className={`max-w-[85%] rounded-2xl rounded-tl-md px-4 py-2.5 text-sm shadow-sm ring-1 ${
          message.error ? 'bg-red-50 text-red-800 ring-red-200' : 'bg-white text-slate-800 ring-slate-200'
        }`}
      >
        <div className="whitespace-pre-wrap leading-relaxed">
          <RichText text={message.content} />
        </div>
        <Sources sources={message.sources} />
      </div>
    </div>
  )
}

export function TypingBubble() {
  return (
    <div className="flex gap-3" aria-label="Assistant is typing">
      <div className="mt-1 flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-brand-100 text-brand-700">
        <SparkIcon className="h-4 w-4" />
      </div>
      <div className="flex items-center gap-1 rounded-2xl rounded-tl-md bg-white px-4 py-3 shadow-sm ring-1 ring-slate-200">
        <span className="typing-dot h-2 w-2 rounded-full bg-slate-400" />
        <span className="typing-dot h-2 w-2 rounded-full bg-slate-400" />
        <span className="typing-dot h-2 w-2 rounded-full bg-slate-400" />
      </div>
    </div>
  )
}
