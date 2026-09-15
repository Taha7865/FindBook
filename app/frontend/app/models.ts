// Mirrors the .NET response. The API decides which books to return and their order.
export type BookEdition = {
  editionId: string;
  title: string;
  publishDate: string | null;
  openLibraryUrl: string;
  subtitle: string | null;
  editionName: string | null;
  contributions: string[];
  notes: string | null;
};

export type BookMatch = {
  openLibraryWorkId: string;
  title: string;
  authors: string[];
  firstPublishYear: number | null;
  openLibraryUrl: string;
  coverUrl: string | null;
  editions: BookEdition[];
  explanation: string;
};

export type SearchResponse = { matches: BookMatch[] };
