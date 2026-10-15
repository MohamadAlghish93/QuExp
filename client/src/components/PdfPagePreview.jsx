import { useEffect, useRef, useState } from 'react'
import * as pdfjsLib from 'pdfjs-dist'
import pdfWorkerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'

pdfjsLib.GlobalWorkerOptions.workerSrc = pdfWorkerUrl

// Renders the first page of a PDF (given as a Blob) onto a plain <canvas> at an
// exact pixel size. Deliberately NOT a native <embed>/<iframe> PDF viewer - those
// come with their own toolbar/chrome that visually overlaps whatever is layered
// on top, and their internal scroll handling can swallow mouse-wheel events even
// through pointer-events: none. A canvas is just pixels - nothing to overlap,
// nothing to capture input, and it scrolls like any other page content.
export default function PdfPagePreview({ blob, width, height, className }) {
  const canvasRef = useRef(null)
  const [error, setError] = useState(null)

  useEffect(() => {
    let cancelled = false
    let renderTask = null

    async function render() {
      setError(null)
      if (!blob || !canvasRef.current) return
      try {
        const arrayBuffer = await blob.arrayBuffer()
        const pdf = await pdfjsLib.getDocument({ data: arrayBuffer }).promise
        if (cancelled) return
        const page = await pdf.getPage(1)

        const baseViewport = page.getViewport({ scale: 1 })
        const scale = Math.min(width / baseViewport.width, height / baseViewport.height)
        const viewport = page.getViewport({ scale })

        const canvas = canvasRef.current
        if (!canvas) return
        canvas.width = viewport.width
        canvas.height = viewport.height

        renderTask = page.render({ canvasContext: canvas.getContext('2d'), viewport })
        await renderTask.promise
      } catch (e) {
        if (!cancelled) setError(e.message || 'Could not render PDF preview.')
      }
    }

    render()
    return () => { cancelled = true; renderTask?.cancel() }
  }, [blob, width, height])

  if (error) return <div className="pdf-preview-error">{error}</div>
  return <canvas ref={canvasRef} className={className} style={{ width, height }} />
}
