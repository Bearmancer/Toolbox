# Search Solutions Comparison for .NET 10 Toolbox App

**Generated:** 2026-07-12 | **Goal:** Determine best search solution for YouTube dashboard + future services

---

## Executive Summary

**Recommendation: PostgreSQL FTS + Meilisearch Hybrid**

For a toolbox app with YouTube metadata, Last.fm scrobbles, and future services, use **PostgreSQL FTS as the primary store** and **Meilisearch for fuzzy/typo-tolerant search** when needed. This hybrid approach minimizes infrastructure while providing excellent search UX.

---

## Comparison Matrix

| Criteria | PostgreSQL FTS | Meilisearch | Typesense | Elasticsearch |
|----------|----------------|-------------|-----------|---------------|
| **Setup Complexity** | Low (built into Postgres) | Low (single binary/Docker) | Low (single binary/Docker) | High (Java, cluster config) |
| **.NET SDK Quality** | Excellent (Npgsql + EF Core) | Good (official MeiliSearch pkg) | Good (community DAXGRID pkg) | Good (Elastic.Clients.Elasticsearch) |
| **Typo Tolerance** | Limited (trigram similarity) | Excellent (native) | Excellent (native) | Excellent (native) |
| **Fuzzy Matching** | Basic (Levenshtein extension) | Excellent (built-in) | Excellent (built-in) | Excellent (fuzzy query) |
| **Resource Usage** | Zero extra (shared Postgres) | ~50MB RAM, 1 CPU | ~100MB RAM, 1 CPU | 1GB+ RAM, 2+ CPU |
| **License** | PostgreSQL License | MIT (CE) / BSL 1.1 (EE) | GPL-3.0 (server) | SSPL / ELv2 / AGPL |
| **Clustering** | Postgres replication | Single-node (CE) | Built-in clustering | Full clustering |
| **Search Speed** | Good (ms) | Excellent (sub-ms) | Excellent (sub-ms) | Excellent (sub-ms) |
| **Learning Curve** | Low (SQL-based) | Low (REST API) | Low (REST API) | High (DSL) |

---

## Detailed Analysis

### 1. PostgreSQL FTS

**Pros:**
- Zero additional infrastructure
- Already in stack if using Postgres
- EF Core integration via Npgsql
- GIN indexes for fast lookups
- No data synchronization needed

**Cons:**
- Limited typo tolerance (requires pg_trgm extension)
- No native fuzzy matching
- Stemming only (no semantic search)
- Query syntax less intuitive than dedicated engines

**.NET Integration:**
```csharp
// EF Core with Npgsql
modelBuilder.Entity<Video>()
    .HasGeneratedTsVectorColumn(
        v => v.SearchVector,
        "english",
        v => new { v.Title, v.Description })
    .HasIndex(v => v.SearchVector)
    .HasMethod("GIN");

// Search query
var results = await context.Videos
    .Where(v => v.SearchVector.Matches(query))
    .OrderByDescending(v => v.SearchVector.Rank(query))
    .ToListAsync();
```

**Best For:** Simple text search, exact matches, already using Postgres

---

### 2. Meilisearch

**Pros:**
- Typo-tolerant out of the box
- Fast setup (single binary)
- Excellent .NET SDK (v0.20.0)
- REST API, easy to use
- MIT license (Community Edition)

**Cons:**
- Single-node only (CE)
- No built-in clustering
- Separate data store (sync required)
- Less mature than Elasticsearch

**.NET Integration:**
```csharp
// NuGet: MeiliSearch
var client = new MeilisearchClient("http://localhost:7700", "masterKey");
var index = client.Index("videos");

// Add documents
await index.AddDocumentsAsync(videos);

// Search with typo tolerance
var results = await index.SearchAsync<Video>("youtbe"); // Finds "youtube"
```

**Best For:** Typo-tolerant search, fast prototyping, small-to-medium datasets

---

### 3. Typesense

**Pros:**
- Typo-tolerant (native)
- Built-in clustering
- Faceted search, geo search
- Good .NET SDK (DAXGRID, v8.4.0)
- Fast performance

**Cons:**
- GPL-3.0 license (server)
- Slightly heavier than Meilisearch
- Community-maintained .NET SDK
- Separate data store (sync required)

**.NET Integration:**
```csharp
// NuGet: Typesense
var client = new TypesenseClient(new Config {
    ApiKey = "xyz",
    Nodes = new List<Node> { new Node("localhost", "8108", "http") }
});

// Create collection
await client.Collections.CreateAsync(new CollectionSchema {
    Name = "videos",
    Fields = new List<Field> {
        new Field { Name = "title", Type = "string" },
        new Field { Name = "description", Type = "string" }
    }
});

// Search
var results = await client.Collections["videos"]
    .Documents.SearchAsync(new SearchParameters {
        Query = "youtbe",  // Typo-tolerant
        QueryBy = "title,description"
    });
```

**Best For:** Clustering needs, geo search, faceted results

---

### 4. Elasticsearch

**Pros:**
- Industry standard
- Powerful query DSL
- Aggregations, analytics
- Full clustering
- Rich ecosystem

**Cons:**
- Heavy (Java, 1GB+ RAM)
- Complex setup
- Licensing complexity (SSPL/ELv2/AGPL)
- Steep learning curve
- Overkill for toolbox app

**.NET Integration:**
```csharp
// NuGet: Elastic.Clients.Elasticsearch
var client = new ElasticsearchClient(new Uri("http://localhost:9200"));

// Search with fuzzy matching
var response = await client.SearchAsync<Video>(s => s
    .Index("videos")
    .Query(q => q
        .MultiMatch(mm => mm
            .Query("youtbe")
            .Fields(f => f.Field(p => p.Title))
            .Fuzziness(Fuzziness.Auto))));
```

**Best For:** Enterprise search, analytics, large-scale systems

---

## Recommendation: Hybrid Approach

### Phase 1: PostgreSQL FTS (Immediate)
- Use for exact/keyword search
- Already in stack, zero cost
- Good enough for YouTube titles, channel names

### Phase 2: Meilisearch (When Needed)
- Add when fuzzy/typo-tolerant search required
- Sync from Postgres via background job
- Best for user-facing search UX

### Implementation Pattern
```
┌─────────────────┐     ┌─────────────────┐
│   PostgreSQL    │────▶│   Meilisearch   │
│   (Primary)     │     │   (Search)      │
└─────────────────┘     └─────────────────┘
        │                       │
        ▼                       ▼
   EF Core Query          REST API Query
   (Exact Match)         (Fuzzy Search)
```

### Why Not Others?
- **Typesense:** Good but GPL license; Meilisearch MIT simpler
- **Elasticsearch:** Overkill for toolbox; heavy infrastructure
- **PostgreSQL alone:** Lacks typo tolerance users expect

---

## Migration Path

1. **Start:** PostgreSQL FTS with GIN indexes
2. **Add:** Meilisearch when user feedback demands fuzzy search
3. **Scale:** Never needed for toolbox app size

---

## References

- [Meilisearch .NET SDK](https://github.com/meilisearch/meilisearch-dotnet)
- [Npgsql FTS Documentation](https://www.npgsql.org/efcore/mapping/full-text-search.html)
- [Typesense .NET Client](https://github.com/DAXGRID/typesense-dotnet)
- [Elasticsearch .NET Client](https://github.com/elastic/elasticsearch-net)
