import { createCliRenderer } from "@opentui/core"
import { createRoot, useKeyboard } from "@opentui/react"
import { useCallback, useRef, useState } from "react"

const MAX_MESSAGES = 500

function App() {
  const [messages, setMessages] = useState<string[]>([])
  const [inputValue, setInputValue] = useState("")
  const [isRunning, setIsRunning] = useState(false)
  const procRef = useRef<any>(null)

  const appendMessage = useCallback((text: string) => {
    if (!text.trim()) return
    setMessages((prev) => {
      const next = [...prev, text]
      if (next.length > MAX_MESSAGES) return next.slice(-MAX_MESSAGES)
      return next
    })
  }, [])

  const runTask = useCallback(
    async (task: string) => {
      if (isRunning || !task.trim()) return
      setIsRunning(true)
      appendMessage(`\u250c\u2500\u2500> ${task}`)

      const script = `
        source /home/grec-alexander/Documents/CS2M/autogen-env/bin/activate
        cd /home/grec-alexander/Documents/CS2M
        python3 ag2_groupchat.py ${JSON.stringify(task)}
      `

      try {
        const proc = Bun.spawn(["bash", "-c", script], {
          stdout: "pipe",
          stderr: "pipe",
        })
        procRef.current = proc

        // Read stdout
        const stdoutReader = proc.stdout.getReader()
        const decoder = new TextDecoder()
        let buffer = ""

        while (true) {
          const { done, value } = await stdoutReader.read()
          if (done) break
          buffer += decoder.decode(value, { stream: true })
          const lines = buffer.split("\n")
          buffer = lines.pop() || ""
          for (const line of lines) {
            const trimmed = line.trim()
            if (!trimmed) continue
            // Filter noise
            if (trimmed.startsWith("Error processing publish")) continue
            if (trimmed.startsWith("Traceback (")) continue
            if (trimmed.startsWith("  File \"/home/grec-alexander/Documents/CS2M/autogen-env")) continue
            if (trimmed.startsWith("    ") && trimmed.includes("async for")) continue
            if (trimmed.startsWith("    ") && trimmed.includes("return await")) continue
            if (trimmed.startsWith("    ") && trimmed.includes("yield ")) continue
            if (trimmed.startsWith("    ") && trimmed.includes("model_result =")) continue
            appendMessage(trimmed)
          }
        }

        // Drain remaining buffer
        if (buffer.trim()) {
          appendMessage(buffer.trim())
        }

        // Read stderr
        const stderrText = await new Response(proc.stderr).text()
        if (stderrText.trim()) {
          const stderrLines = stderrText.split("\n")
          for (const line of stderrLines) {
            const trimmed = line.trim()
            if (!trimmed) continue
            if (trimmed.startsWith("Error processing publish")) continue
            if (trimmed.startsWith("Traceback")) continue
            if (trimmed.startsWith("  File \"/home/grec-alexander/Documents/CS2M/autogen-env")) continue
            appendMessage(`[stderr] ${trimmed}`)
          }
        }

        appendMessage("\u2514\u2500\u2500 done")
      } catch (e: any) {
        appendMessage(`[ERROR] ${e.message || e}`)
      } finally {
        setIsRunning(false)
        procRef.current = null
      }
    },
    [isRunning, appendMessage]
  )

  useKeyboard((key) => {
    if (key.name === "c" && key.ctrl) {
      if (procRef.current) {
        try {
          procRef.current.kill()
        } catch {}
      }
      process.exit(0)
    }
    if (key.name === "return") {
      if (!isRunning && inputValue.trim()) {
        const task = inputValue
        setInputValue("")
        runTask(task)
      }
    }
  })

  const statusColor = isRunning ? "#f59e0b" : "#22c55e"
  const statusText = isRunning ? "running" : "ready"

  return (
    <box style={{ flexDirection: "column", flexGrow: 1 }}>
      <box
        style={{
          flexGrow: 1,
          border: true,
          title: " CS2M Multi-Agent Orchestrator ",
          borderStyle: "double",
        }}
      >
        <scrollbox style={{ flexGrow: 1, padding: 1 }}>
          {messages.length === 0 && (
            <text style={{ fg: "#666" }}>
              Type a task and press Enter. Ctrl+C to exit.
            </text>
          )}
          {messages.map((msg, i) => {
            const isUser = msg.startsWith("> ")
            const isError = msg.startsWith("[ERROR]") || msg.startsWith("[stderr]")
            const isDone = msg === "\u2514\u2500\u2500 done"
            return (
              <text
                key={i}
                style={{
                  marginBottom: 1,
                  wordWrap: true,
                  fg: isUser
                    ? "#60a5fa"
                    : isError
                      ? "#ef4444"
                      : isDone
                        ? "#22c55e"
                        : "#e5e5e5",
                }}
              >
                {msg}
              </text>
            )
          })}
        </scrollbox>
      </box>
      <box
        style={{
          height: 3,
          border: true,
          flexDirection: "row",
          alignItems: "center",
          paddingLeft: 1,
          paddingRight: 1,
        }}
      >
        <text style={{ width: 3, fg: statusColor }}>
          {isRunning ? "\u25cf " : "\u25cb "}
        </text>
        <text style={{ width: 10, fg: statusColor }}>{statusText}</text>
        <text style={{ width: 2 }}>"> </text>
        <input
          style={{ flexGrow: 1 }}
          placeholder={isRunning ? "Running..." : "Type task + Enter"}
          value={inputValue}
          onInput={setInputValue}
          focused
        />
      </box>
    </box>
  )
}

const renderer = await createCliRenderer({
  exitOnCtrlC: false,
})
createRoot(renderer).render(<App />)
