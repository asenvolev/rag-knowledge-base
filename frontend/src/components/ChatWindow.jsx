import { useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import MessageBubble, { TypingBubble } from './MessageBubble'
import { SendIcon, XIcon } from './Icons'

const SUGGESTIONS = [
  'Summarize the key points of this document',
  'What are the main deadlines or dates mentioned?',
  'List any obligations or responsibilities',
]

export default function ChatWindow({ scopeDocument, hasDocuments, onClearScope }) {
  const [messages, setMessages] = useState([])
  const [input, setInput] = useState('')
  const [sending, setSending] = useState(false)
  const scrollRef = useRef(null)
  const textareaRef = useRef(null)
  const abortRef = useRef(null)

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' })
  }, [messages, sending])

  useEffect(() => () => abortRef.current?.abort(), [])

  // Auto-grow textarea up to ~6 lines.
  useEffect(() => {
    const el = textareaRef.current
    if (!el) return
    el.style.height = 'auto'
    el.style.height = `${Math.min(el.scrollHeight, 160)}px`
  }, [input])

  async function send(text = input) {
    const question = text.trim()
    if (!question || sending) return

    // Only successful turns are sent back as conversation history.
    const history = messages
      .filter((m) => !m.error)
      .map(({ role, content }) => ({ role, content }))
      .slice(-6)

    setMessages((m) => [...m, { id: crypto.randomUUID(), role: 'user', content: question }])
    setInput('')
    setSending(true)

    abortRef.current = new AbortController()
    try {
      const res = await api.chat(
        { message: question, documentId: scopeDocument?.id ?? null, history },
        abortRef.current.signal,
      )
      setMessages((m) => [...m, { id: crypto.randomUUID(), role: 'assistant', content: res.answer, sources: res.sources }])
    } catch (err) {
      setMessages((m) => [...m, { id: crypto.randomUUID(), role: 'assistant', content: err.message, error: true }])
    } finally {
      setSending(false)
      textareaRef.current?.focus()
    }
  }

  function onKeyDown(e) {
    if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault()
      send()
    }
  }

  return (
    <section className="flex h-full min-h-0 flex-col">
      <header className="flex items-center justify-between gap-3 border-b border-slate-200 bg-white px-4 py-3 md:px-6">
        <div className="min-w-0">
          <h2 className="text-sm font-semibold text-slate-900">Ask your documents</h2>
          <p className="truncate text-xs text-slate-500">
            {scopeDocument ? <>Searching in <span className="font-medium text-slate-700">{scopeDocument.fileName}</span></> : 'Searching across all documents'}
          </p>
        </div>
        <div className="flex items-center gap-2">
          {scopeDocument && (
            <button
              onClick={onClearScope}
              className="flex items-center gap-1 rounded-lg px-2 py-1 text-xs text-slate-600 ring-1 ring-slate-200 hover:bg-slate-50"
            >
              <XIcon className="h-3 w-3" /> All docs
            </button>
          )}
          {messages.length > 0 && (
            <button
              onClick={() => setMessages([])}
              disabled={sending}
              className="rounded-lg px-2 py-1 text-xs text-slate-600 ring-1 ring-slate-200 hover:bg-slate-50 disabled:opacity-50"
            >
              New chat
            </button>
          )}
        </div>
      </header>

      <div ref={scrollRef} className="min-h-0 flex-1 overflow-y-auto px-4 py-6 md:px-6">
        {messages.length === 0 ? (
          <div className="mx-auto flex h-full max-w-md flex-col items-center justify-center text-center">
            <h3 className="text-lg font-semibold text-slate-900">What do you want to know?</h3>
            <p className="mt-1 text-sm text-slate-500">
              {hasDocuments
                ? 'Answers come only from your uploaded PDFs, with page-level sources.'
                : 'Upload a PDF on the left first — then ask anything about it.'}
            </p>
            {hasDocuments && (
              <div className="mt-6 flex w-full flex-col gap-2">
                {SUGGESTIONS.map((s) => (
                  <button
                    key={s}
                    onClick={() => send(s)}
                    className="rounded-xl bg-white px-4 py-2.5 text-left text-sm text-slate-700 shadow-sm ring-1 ring-slate-200 transition hover:ring-brand-500"
                  >
                    {s}
                  </button>
                ))}
              </div>
            )}
          </div>
        ) : (
          <div className="mx-auto max-w-3xl space-y-5">
            {messages.map((m) => <MessageBubble key={m.id} message={m} />)}
            {sending && <TypingBubble />}
          </div>
        )}
      </div>

      <div className="border-t border-slate-200 bg-white px-4 py-3 md:px-6">
        <form
          onSubmit={(e) => { e.preventDefault(); send() }}
          className="mx-auto flex max-w-3xl items-end gap-2 rounded-2xl bg-slate-50 p-2 ring-1 ring-slate-200 focus-within:ring-brand-500"
        >
          <textarea
            ref={textareaRef}
            rows={1}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={onKeyDown}
            maxLength={4000}
            placeholder={hasDocuments ? 'Ask a question… (Shift+Enter for a new line)' : 'Upload a document to start'}
            disabled={!hasDocuments}
            className="max-h-40 flex-1 resize-none bg-transparent px-2 py-1.5 text-sm placeholder:text-slate-400 focus:outline-none disabled:cursor-not-allowed"
          />
          <button
            type="submit"
            disabled={!input.trim() || sending || !hasDocuments}
            aria-label="Send"
            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-brand-600 text-white transition hover:bg-brand-700 disabled:cursor-not-allowed disabled:bg-slate-300"
          >
            <SendIcon className="h-4 w-4" />
          </button>
        </form>
      </div>
    </section>
  )
}
