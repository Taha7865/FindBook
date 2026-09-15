// Forward requests on the server so the browser uses one origin and never receives the Gemini key.
export async function POST(request: Request) {
  const apiBaseUrl = process.env.API_BASE_URL || "http://127.0.0.1:8080";
  const timeout = AbortSignal.timeout(240_000);

  try {
    const response = await fetch(new URL("/api/books/search", apiBaseUrl), {
      method: "POST",
      headers: { "Content-Type": request.headers.get("content-type") || "application/json" },
      body: await request.text(),
      cache: "no-store",
      signal: AbortSignal.any([request.signal, timeout]),
    });
    const contentType = response.headers.get("content-type") || "";
    if (!contentType.includes("json")) {
      return Response.json({ title: "Search returned an unexpected response. Please try again." }, { status: 502 });
    }
    return new Response(await response.text(), {
      status: response.status,
      headers: { "Content-Type": contentType, "Cache-Control": "no-store" },
    });
  } catch {
    return Response.json({
      title: timeout.aborted ? "Search took too long. Please try again." : "The search service is unavailable. Please try again later.",
    }, { status: timeout.aborted ? 504 : 503 });
  }
}
