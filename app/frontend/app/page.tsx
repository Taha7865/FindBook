"use client";

import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent, type MouseEvent } from "react";
import BookResult from "./book-result";
import type { SearchResponse } from "./models";

export default function Home() {
  const [query, setQuery] = useState("");
  const [submittedQuery, setSubmittedQuery] = useState("");
  const [result, setResult] = useState<SearchResponse | null>(null);
  const [isSearching, setIsSearching] = useState(false);
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const pendingRequest = useRef<AbortController | null>(null);
  const input = useRef<HTMLTextAreaElement>(null);
  const resultsHeading = useRef<HTMLHeadingElement>(null);
  const hasSearch = submittedQuery.length > 0;

  useEffect(() => () => pendingRequest.current?.abort(), []);
  useEffect(() => {
    if (result) resultsHeading.current?.focus({ preventScroll: true });
  }, [result]);

  async function search() {
    const text = query.trim();
    if (!text || pendingRequest.current) return;
    const controller = new AbortController();
    pendingRequest.current = controller;
    setSubmittedQuery(text);
    setIsSearching(true);
    setResult(null);
    setError("");
    setStatus("Searching for your book…");

    try {
      const response = await fetch("/api/books/search", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ query: text }),
        signal: controller.signal,
      });
      const body = await response.json();
      if (!response.ok) throw new Error(body.title || "Search is unavailable. Please try again.");
      if (!Array.isArray(body.matches)) throw new Error("Search returned an unexpected response. Please try again.");
      if (controller.signal.aborted) return;
      setResult(body as SearchResponse);
      setStatus("");
    } catch (error) {
      if (!controller.signal.aborted) {
        setError(error instanceof Error && error.name !== "TypeError" && error.name !== "SyntaxError"
          ? error.message : "Could not reach search. Check your connection and try again.");
        setStatus("");
      }
    } finally {
      if (pendingRequest.current === controller) {
        pendingRequest.current = null;
        setIsSearching(false);
      }
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void search();
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    // Enter submits; Shift+Enter adds a line. Leave composition input (such as Japanese) alone.
    if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      void search();
    }
  }

  function cancelSearch(event: MouseEvent<HTMLButtonElement>) {
    // Do not submit again if this button switches back to the send button during the click.
    event.preventDefault();
    pendingRequest.current?.abort();
    setStatus("Search stopped. You can edit your words and try again.");
  }

  function newSearch() {
    if (pendingRequest.current) return;
    setQuery("");
    setSubmittedQuery("");
    setResult(null);
    setError("");
    setStatus("");
    input.current?.focus();
  }

  return (
    <div className={`app-shell ${hasSearch ? "has-search" : ""}`}>
      <header className="topbar">
        <span className="brand"><svg width="22" height="22" viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M3 4h5c2 0 4 1 4 3 0-2 2-3 4-3h5v15h-5c-2 0-4 1-4 2 0-1-2-2-4-2H3V4Zm9 3v14" stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round" /></svg>Find That Book</span>
        {hasSearch && <button className="new-search" onClick={newSearch} disabled={isSearching}>New search <span aria-hidden="true">↗</span></button>}
      </header>

      <main className="main">
        <section className="search-area" aria-labelledby="page-title">
          <p className="eyebrow">A little memory. A new discovery.</p>
          <h1 id="page-title">A book on your mind?</h1>
          <p className="intro">Start with a title, an author, or the details you remember.</p>
          <form onSubmit={submit} className="search-form">
            <label htmlFor="book-query" className="sr-only">Describe the book you want to find</label>
            <textarea ref={input} id="book-query" name="query" value={query} rows={2} maxLength={1000}
              placeholder="A title, an author, or something you remember…"
              onChange={event => setQuery(event.target.value)} onKeyDown={handleKeyDown}
              disabled={isSearching} aria-describedby="input-hint" />
            <div className="search-actions">
              <span id="input-hint">{query.length > 800 ? `${query.length} / 1,000` : "Enter to search · Shift + Enter for a new line"}</span>
              {isSearching ? (
                <button type="button" className="search-button" aria-label="Stop search" onClick={cancelSearch}><span className="stop-icon" />Stop search</button>
              ) : (
                <button type="submit" className="search-button" aria-label="Search books" disabled={!query.trim()}>
                  <svg width="22" height="22" viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle cx="10.5" cy="10.5" r="6.5" stroke="currentColor" strokeWidth="1.8" /><path d="m16 16 4 4" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" /></svg>Search books
                </button>
              )}
            </div>
          </form>
        </section>

        {hasSearch && (
          <section className="search-results" aria-label="Search results">
            <p className="search-context">Results for <span>“{submittedQuery}”</span></p>
            <p role="status" aria-live="polite" className={status ? "search-status" : "sr-only"}>
              {isSearching && <span className="loading-dot" aria-hidden="true" />}{status}
            </p>
            {error && <div role="alert" className="error-message"><h2>We couldn’t finish that search.</h2><p>{error}</p><p>Your query is still above. Press Enter or select Search books to try again.</p></div>}
            {result && <div className="results">
              <h2 ref={resultsHeading} tabIndex={-1}>
                {result.matches.length === 0 ? "No matching books found." : result.matches.length === 1 ? "One book to explore" : `${result.matches.length} books to explore`}
              </h2>
              {result.matches.length === 0 ? <p className="empty-message">Try a shorter title, an author’s name, or a different detail. You can also leave out the year to broaden the search.</p>
                : <ol className="result-list" aria-label="Books in match order">{result.matches.map((book, index) => (
                  <li key={book.openLibraryWorkId}><BookResult book={book} rank={index + 1} showRank={result.matches.length > 1} /></li>
                ))}</ol>}
            </div>}
          </section>
        )}
      </main>
      <footer>Find a book. Follow your curiosity.<span>Book details from <a href="https://openlibrary.org" target="_blank" rel="noopener noreferrer">Open Library</a>. Matches can be imperfect.</span></footer>
    </div>
  );
}
