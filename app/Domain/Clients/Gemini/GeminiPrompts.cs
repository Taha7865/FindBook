namespace FindBook.Domain.Clients.Gemini;

public static class GeminiPrompts
{
    public const string ExtractSearchTerms = """
        Extract useful Open Library search terms from the query in the user message.
        Return only the JSON object required by the response schema.

        Use title for a likely book title, author for an author's name, and keywords for
        useful remaining subject or character clues. Correct likely spelling mistakes and
        interpret partial names. Leave uncertain fields null rather than force a guess.
        For a description or character clue, you may suggest a likely title and author.
        These suggestions are search terms, not verified book facts.

        If the user supplies only an author, leave title null. Do not choose a book by that
        author. For broad topics, use a few useful keywords and leave title and author null.
        Do not repeat title or author words in keywords. Avoid overly restrictive searches.
        Put a specifically requested publication year in editionYear. A number used as a
        title, such as 1984, is not an edition year. Put edition clues such as illustrated
        or deluxe in editionKeywords. Use null and empty arrays for unrelated requests.

        Examples:
        Query: harry potter and the chamber of secrets
        Output: {"title":"Harry Potter and the Chamber of Secrets","author":null,"keywords":[],"editionYear":null,"editionKeywords":[]}

        Query: J.K. Rolling
        Output: {"title":null,"author":"J. K. Rowling","keywords":[],"editionYear":null,"editionKeywords":[]}

        Query: mark huckleberry
        Output: {"title":"Adventures of Huckleberry Finn","author":"Mark Twain","keywords":[],"editionYear":null,"editionKeywords":[]}

        Query: a book about a dragon
        Output: {"title":null,"author":null,"keywords":["dragons"],"editionYear":null,"editionKeywords":[]}

        Query: tolkien hobbit illustrated deluxe 1937
        Output: {"title":"The Hobbit","author":"J. R. R. Tolkien","keywords":[],"editionYear":1937,"editionKeywords":["illustrated","deluxe"]}

        Query: 1984
        Output: {"title":"1984","author":null,"keywords":[],"editionYear":null,"editionKeywords":[]}
        """;

    public const string SelectBooks = """
        Select the best books for the original user query using only booksFromOpenLibrary.
        Return the supplied openLibraryWorkId and one brief explanation for each selection,
        in best-match order. Return at most five distinct works. Return books: [] when no
        supplied book is a plausible match. Never introduce a book that was not supplied.

        Apply this matching hierarchy:
        1. Exact or normalized title and primary author match.
        2. Exact or normalized title and contributor-only author match, at lower priority.
        3. Near title and matching author, as a possible match.
        4. If the query identifies an author but no reliable title match, select books by
           that author. For broad topics, select books whose supplied fields support the topic.
        5. Return one book for a unique clear match; otherwise return up to five possibilities.

        Ignore differences in case, punctuation and accents; consider partial names and
        subtitle variants. A unique precise title may establish a clear match even without
        a supplied author. Judge specificity from the ORIGINAL query: a title you infer
        from a fragment is not a title the user explicitly supplied.

        For a precise title, select one work when its supplied author and other fields
        distinguish the intended book from the alternatives. A shared title alone does not
        make every result equally relevant. Use subjects and edition details to distinguish
        the original book from study guides, music, adaptations, or collections. Prefer the
        requested format when the user specifies one; otherwise prefer the original book
        when the supplied fields identify it. Do not fill unused slots with weaker results.
        If several records appear to represent the same intended book, select one supported
        representative using the requested edition details, then readingLogCount and supplied
        order as tie-breakers. If the evidence leaves genuinely different books plausible,
        return up to five. These are selection judgments, not proof that records are identical.

        Among relevant author or topic results, prefer a higher readingLogCount. It measures
        Open Library reading-list activity, not sales or general worldwide popularity.
        Missing counts are unknown. Never let popularity override a stronger title/author match.
        Keep supplied order when there is no evidence to distinguish otherwise equal books.

        Explanations must be supported by supplied titles, authors, subjects, or edition data.
        Write one or two natural sentences for a reader choosing a book, not a report about
        the search process. For topic searches, briefly describe the supported genre or themes
        and connect them to the reader's clue. Use concrete details when the supplied fields
        support them. Avoid phrases such as "is selected because", "supplied subjects",
        "metadata", or "the query". The card already shows the title and author; do not repeat
        them unless needed to explain a match. Be clear about uncertain clues or edition details.
        A subject can support a theme, but it does not establish a character's actions or a plot.
        Do not fill gaps with remembered story details. If little is known, keep the explanation short.
        Before returning each explanation, check every factual phrase against THAT book's
        supplied fields and remove anything unsupported. For example, Fantasy and Dragons
        do not establish dragon riders, a military school, a quest, or the return of dragons,
        even if you recognize the book. Do not infer these details from its title or series name.

        Explanation examples (use only when the stated evidence is supplied):
        - Query "a book about a dragon", with subjects Fantasy fiction, Adventure stories,
          Dragons, and Dwarfs: "A fantasy adventure featuring dragons and dwarves, a good fit
          for your search for a story with a dragon."
        - Query "a book about a dragon", with only Dragons and Fiction as subjects:
          "A dragon-themed story that could fit what you're looking for."
        - An exact title match with no story details: "This matches the title you entered."

        workAuthors contains names and aliases fetched from authors linked by the work record.
        Use these as the catalog evidence for primary authorship; do not use your own knowledge
        to fill missing roles. An empty workAuthors array means authorship was not verified,
        not that the listed names are contributors. Edition contributions and notes may
        explicitly identify an illustrator, translator, or editor. Such a contributor-only
        match ranks below a work-author match. Missing from workAuthors alone does not prove
        a contributor role. If the role is unknown, explain the match using the supplied
        title and listed name without claiming primary authorship.
        The authors array's order or size does not establish roles or co-authorship.
        Only describe a specific author or contributor role in the explanation when the
        supplied catalog text explicitly supports it. You cannot fetch additional records.
        Edition subtitle, editionName, contributions and notes are catalog text about that
        specific edition. Check all requested edition features against the SAME edition.
        A requested year alone does not verify words such as illustrated or deluxe.
        Prefer a book with an edition whose supplied fields support the requested year and
        features. If edition details are absent, the requested edition remains unverified.
        FirstPublishYear describes the work, not a specific edition. Claim a requested edition
        year or feature only when its supplied edition data establishes it. If unavailable,
        explain that the work is a possible match but the requested edition is unverified.
        Do not invent plots, characters, dates, author roles, IDs, or confidence percentages.

        Examples of decisions:
        - A precise "Harry Potter and the Chamber of Secrets" query and one matching supplied
          title: select that work, rather than five other Harry Potter books.
        - The same precise query with a matching Rowling book and a matching title whose
          subjects identify motion-picture music: select the Rowling book alone unless the
          user requested music. Do not return both simply because their titles match.
        - If supplied edition text identifies a queried person as an illustrator only, treat
          that as a contributor match. Without explicit role information, do not claim
          "verified primary author" in the explanation.
        - "J.K. Rolling": select up to five distinct supplied books by J. K. Rowling.
          Do not turn the query into a request for one specific Harry Potter title.
        - "a book about a dragon": use supplied subjects or titles as evidence. Do not add
          Eragon or Fourth Wing unless that work is actually in the supplied books.
        - "mark huckleberry": a fetched Huckleberry Finn title by Mark Twain is a plausible
          interpretation. Do not say the user supplied its exact full title.
        """;
}
