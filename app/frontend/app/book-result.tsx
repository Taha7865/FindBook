"use client";

import { useState } from "react";
import type { BookMatch } from "./models";

export default function BookResult({ book }: { book: BookMatch }) {
  const [coverFailed, setCoverFailed] = useState(false);

  return (
    <article className="book-result">
      <div className="book-cover">
        {book.coverUrl && !coverFailed ? (
          <img src={book.coverUrl} alt={`Cover of ${book.title}`} width="104" height="156"
            loading="lazy" referrerPolicy="no-referrer" onError={() => setCoverFailed(true)} />
        ) : <div className="cover-placeholder" aria-label="Cover unavailable"><span aria-hidden="true">{book.title}</span><small>Cover unavailable</small></div>}
      </div>
      <div className="book-details">
        {book.firstPublishYear !== null && <div className="book-meta">First published {book.firstPublishYear}</div>}
        <h3><a href={book.openLibraryUrl} target="_blank" rel="noopener noreferrer">{book.title}</a></h3>
        {book.primaryAuthor && <p className="book-author">Primary author: {book.primaryAuthor}</p>}
        {(!book.primaryAuthor || book.authors.length > 1) && (
          <p className="book-author">{book.authors.length ? book.authors.join(", ") : "Author not listed"}</p>
        )}
        <p className="match-explanation">{book.explanation}</p>
        <a className="catalog-link" href={book.openLibraryUrl} target="_blank" rel="noopener noreferrer">View book <span aria-hidden="true">↗</span></a>
        {book.editions.length > 0 && (
          <details className="editions">
            <summary>Edition details <span>({book.editions.length})</span></summary>
            <ul>{book.editions.map(edition => (
              <li key={edition.editionId}>
                <a href={edition.openLibraryUrl} target="_blank" rel="noopener noreferrer">{edition.title} ↗</a>
                {edition.subtitle && <p>{edition.subtitle}</p>}
                <p>{edition.publishDate ? `Published ${edition.publishDate}` : "Publication date not available"}{edition.editionName ? ` · ${edition.editionName}` : ""}</p>
                {edition.contributions.length > 0 && <p>{edition.contributions.join("; ")}</p>}
                {edition.notes && <p>{edition.notes}</p>}
              </li>
            ))}</ul>
          </details>
        )}
      </div>
    </article>
  );
}
