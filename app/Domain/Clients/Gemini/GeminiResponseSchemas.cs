using System.Text.Json;

namespace FindBook.Domain.Clients.Gemini;

internal static class GeminiResponseSchemas
{
    public static readonly JsonElement SearchTerms = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "title": { "type": ["string", "null"] },
            "author": { "type": ["string", "null"] },
            "keywords": { "type": "array", "items": { "type": "string" }, "maxItems": 8 },
            "editionYear": { "type": ["integer", "null"], "minimum": 1, "maximum": 9999 },
            "editionKeywords": { "type": "array", "items": { "type": "string" }, "maxItems": 5 }
          },
          "required": ["title", "author", "keywords", "editionYear", "editionKeywords"],
          "additionalProperties": false
        }
        """);

    public static readonly JsonElement Selection = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "books": {
              "type": "array", "maxItems": 5,
              "items": {
                "type": "object",
                "properties": {
                  "openLibraryWorkId": { "type": "string" },
                  "explanation": { "type": "string" }
                },
                "required": ["openLibraryWorkId", "explanation"],
                "additionalProperties": false
              }
            }
          },
          "required": ["books"],
          "additionalProperties": false
        }
        """);
}
