-- ============================================================
--  PhotoVault — SQLite Schema  (Phase 1)
--  Stored at:  /MediaRoot/Application/Database/photovault.db
--  Engine:     SQLite 3.x  (WAL mode, FK enforcement on)
-- ============================================================

PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;
PRAGMA auto_vacuum  = INCREMENTAL;

-- ── 1. USERS ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS Users (
    Id              TEXT    PRIMARY KEY,          -- Google sub (stable)
    Email           TEXT    NOT NULL UNIQUE,
    DisplayName     TEXT    NOT NULL,
    PhotoUrl        TEXT,
    Role            TEXT    NOT NULL DEFAULT 'Pending'
                            CHECK (Role IN ('Admin','User','Pending','Rejected')),
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now','utc')),
    ApprovedAt      TEXT,
    ApprovedBy      TEXT,                         -- Admin User.Id
    LastLoginAt     TEXT,
    IsActive        INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS idx_users_email  ON Users(Email);
CREATE INDEX IF NOT EXISTS idx_users_role   ON Users(Role);

-- ── 2. MEDIA ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS Media (
    Id              TEXT    PRIMARY KEY,           -- UUID v4
    FileName        TEXT    NOT NULL,
    OriginalPath    TEXT    NOT NULL UNIQUE,       -- relative to /PhotosVideos/
    FileHash        TEXT    NOT NULL,              -- SHA-256 for dupe detection
    PerceptualHash  TEXT,                          -- pHash for near-dupe detection
    MediaType       TEXT    NOT NULL
                            CHECK (MediaType IN ('Photo','Video','Unknown')),
    MimeType        TEXT,
    FileSizeBytes   INTEGER NOT NULL DEFAULT 0,
    Width           INTEGER,
    Height          INTEGER,
    DurationSeconds REAL,                          -- videos only
    CapturedAt      TEXT,                          -- EXIF DateTimeOriginal (UTC)
    Latitude        REAL,
    Longitude       REAL,
    CameraModel     TEXT,
    IsBlurry        INTEGER NOT NULL DEFAULT 0,
    BlurScore       REAL,
    IsDuplicate     INTEGER NOT NULL DEFAULT 0,
    DuplicateOfId   TEXT    REFERENCES Media(Id),
    InTrash         INTEGER NOT NULL DEFAULT 0,
    TrashedAt       TEXT,
    TrashedByUserId TEXT    REFERENCES Users(Id),
    TrashPath       TEXT,                          -- path under /Application/Trash/
    RestoredAt      TEXT,
    AIProcessed     INTEGER NOT NULL DEFAULT 0,
    AIProcessedAt   TEXT,
    AIModelUsed     TEXT,
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now','utc')),
    UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now','utc'))
);
CREATE INDEX IF NOT EXISTS idx_media_hash        ON Media(FileHash);
CREATE INDEX IF NOT EXISTS idx_media_phash       ON Media(PerceptualHash);
CREATE INDEX IF NOT EXISTS idx_media_captured    ON Media(CapturedAt);
CREATE INDEX IF NOT EXISTS idx_media_type        ON Media(MediaType);
CREATE INDEX IF NOT EXISTS idx_media_trash       ON Media(InTrash);
CREATE INDEX IF NOT EXISTS idx_media_ai          ON Media(AIProcessed);
CREATE INDEX IF NOT EXISTS idx_media_latlon      ON Media(Latitude, Longitude);

-- Auto-update UpdatedAt
CREATE TRIGGER IF NOT EXISTS trg_media_updated
AFTER UPDATE ON Media FOR EACH ROW
BEGIN
    UPDATE Media SET UpdatedAt = datetime('now','utc') WHERE Id = OLD.Id;
END;

-- ── 3. THUMBNAILS ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS Thumbnails (
    Id          TEXT    PRIMARY KEY,
    MediaId     TEXT    NOT NULL REFERENCES Media(Id) ON DELETE CASCADE,
    Size        TEXT    NOT NULL CHECK (Size IN ('sm','md','lg')),
    Path        TEXT    NOT NULL,                 -- relative to /Application/Thumbnails/
    Width       INTEGER NOT NULL,
    Height      INTEGER NOT NULL,
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now','utc')),
    UNIQUE(MediaId, Size)
);
CREATE INDEX IF NOT EXISTS idx_thumbnails_media ON Thumbnails(MediaId);

-- ── 4. TAGS ──────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS Tags (
    Id          TEXT    PRIMARY KEY,
    Name        TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    Category    TEXT    NOT NULL DEFAULT 'General'
                        CHECK (Category IN ('Object','Person','Place',
                                            'Event','Emotion','General','AI','User')),
    IsAIGenerated INTEGER NOT NULL DEFAULT 0,
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now','utc'))
);
CREATE INDEX IF NOT EXISTS idx_tags_name     ON Tags(Name COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS idx_tags_category ON Tags(Category);

-- ── 5. MEDIA TAGS (join) ─────────────────────────────────────
CREATE TABLE IF NOT EXISTS MediaTags (
    MediaId     TEXT    NOT NULL REFERENCES Media(Id) ON DELETE CASCADE,
    TagId       TEXT    NOT NULL REFERENCES Tags(Id)  ON DELETE CASCADE,
    Confidence  REAL    DEFAULT 1.0,
    Source      TEXT    NOT NULL DEFAULT 'User'
                        CHECK (Source IN ('User','AI','System')),
    AddedByUserId TEXT  REFERENCES Users(Id),
    AddedAt     TEXT    NOT NULL DEFAULT (datetime('now','utc')),
    PRIMARY KEY (MediaId, TagId)
);
CREATE INDEX IF NOT EXISTS idx_mediatags_media ON MediaTags(MediaId);
CREATE INDEX IF NOT EXISTS idx_mediatags_tag   ON MediaTags(TagId);

-- ── 6. ALBUMS ────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS Albums (
    Id              TEXT    PRIMARY KEY,
    Name            TEXT    NOT NULL,
    Description     TEXT,
    CoverMediaId    TEXT    REFERENCES Media(Id),
    AlbumType       TEXT    NOT NULL DEFAULT 'User'
                            CHECK (AlbumType IN ('User','AI','Smart','System')),
    CreatedByUserId TEXT    REFERENCES Users(Id),
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now','utc')),
    UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now','utc')),
    IsShared        INTEGER NOT NULL DEFAULT 0,
    ShareToken      TEXT    UNIQUE                -- future sharing
);

-- ── 7. ALBUM MEDIA (join) ────────────────────────────────────
CREATE TABLE IF NOT EXISTS AlbumMedia (
    AlbumId     TEXT    NOT NULL REFERENCES Albums(Id) ON DELETE CASCADE,
    MediaId     TEXT    NOT NULL REFERENCES Media(Id)  ON DELETE CASCADE,
    SortOrder   INTEGER NOT NULL DEFAULT 0,
    AddedAt     TEXT    NOT NULL DEFAULT (datetime('now','utc')),
    PRIMARY KEY (AlbumId, MediaId)
);
CREATE INDEX IF NOT EXISTS idx_albummedia_album ON AlbumMedia(AlbumId);
CREATE INDEX IF NOT EXISTS idx_albummedia_media ON AlbumMedia(MediaId);

-- ── 8. FACE CLUSTERS ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS FaceClusters (
    Id              TEXT    PRIMARY KEY,
    Label           TEXT,                         -- user-assigned name
    CoverMediaId    TEXT    REFERENCES Media(Id),
    RepresentativeEmbedding TEXT,                 -- base64 float[] centroid
    MediaCount      INTEGER NOT NULL DEFAULT 0,
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now','utc')),
    UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now','utc'))
);

CREATE TABLE IF NOT EXISTS FaceClusterMembers (
    Id              TEXT    PRIMARY KEY,
    ClusterId       TEXT    NOT NULL REFERENCES FaceClusters(Id) ON DELETE CASCADE,
    MediaId         TEXT    NOT NULL REFERENCES Media(Id)        ON DELETE CASCADE,
    BoundingBoxJson TEXT,                         -- {x,y,w,h} normalised 0-1
    Embedding       TEXT,                         -- base64 float[]
    Confidence      REAL    DEFAULT 1.0,
    UNIQUE (ClusterId, MediaId)
);
CREATE INDEX IF NOT EXISTS idx_fcm_cluster ON FaceClusterMembers(ClusterId);
CREATE INDEX IF NOT EXISTS idx_fcm_media   ON FaceClusterMembers(MediaId);

-- ── 9. EMBEDDINGS ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS Embeddings (
    Id          TEXT    PRIMARY KEY,
    MediaId     TEXT    NOT NULL UNIQUE REFERENCES Media(Id) ON DELETE CASCADE,
    ModelName   TEXT    NOT NULL,                 -- e.g. text-embedding-3-small
    Vector      BLOB    NOT NULL,                 -- raw float32[] little-endian
    Dimensions  INTEGER NOT NULL,
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now','utc'))
);
CREATE INDEX IF NOT EXISTS idx_embeddings_media ON Embeddings(MediaId);

-- ── 10. AI PROCESSING LOGS ───────────────────────────────────
CREATE TABLE IF NOT EXISTS AIProcessingLogs (
    Id          TEXT    PRIMARY KEY,
    MediaId     TEXT    NOT NULL REFERENCES Media(Id) ON DELETE CASCADE,
    Stage       TEXT    NOT NULL
                        CHECK (Stage IN ('Thumbnail','Metadata','Tagging',
                                         'FaceDetection','Embedding','DupeCheck','BlurCheck')),
    Status      TEXT    NOT NULL DEFAULT 'Pending'
                        CHECK (Status IN ('Pending','Running','Success','Failed','Skipped')),
    ModelUsed   TEXT,
    AttemptCount INTEGER NOT NULL DEFAULT 0,
    StartedAt   TEXT,
    CompletedAt TEXT,
    ErrorMessage TEXT,
    TokensUsed  INTEGER,
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now','utc'))
);
CREATE INDEX IF NOT EXISTS idx_ailogs_media  ON AIProcessingLogs(MediaId);
CREATE INDEX IF NOT EXISTS idx_ailogs_stage  ON AIProcessingLogs(Stage);
CREATE INDEX IF NOT EXISTS idx_ailogs_status ON AIProcessingLogs(Status);

-- ── 11. APP SETTINGS ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS AppSettings (
    Key         TEXT    PRIMARY KEY,
    Value       TEXT    NOT NULL,
    IsEncrypted INTEGER NOT NULL DEFAULT 0,
    UpdatedAt   TEXT    NOT NULL DEFAULT (datetime('now','utc'))
);

-- Default settings
INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES
    ('ai.default_model',        'openai'),
    ('ai.auto_tag_enabled',     'true'),
    ('ai.batch_size',           '10'),
    ('thumbnail.sm_width',      '200'),
    ('thumbnail.md_width',      '400'),
    ('thumbnail.lg_width',      '800'),
    ('storage.mediaroot',       ''),             -- set on first boot
    ('app.initialized',         'false'),
    ('app.version',             '1.0.0');

-- ── 12. MEDIA CAPTIONS ───────────────────────────────────────
CREATE TABLE IF NOT EXISTS MediaCaptions (
    Id          TEXT    PRIMARY KEY,
    MediaId     TEXT    NOT NULL UNIQUE REFERENCES Media(Id) ON DELETE CASCADE,
    Caption     TEXT    NOT NULL,
    ModelUsed   TEXT,
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now','utc'))
);

-- ── 13. HIGHLIGHTS / MEMORIES ────────────────────────────────
CREATE TABLE IF NOT EXISTS Highlights (
    Id          TEXT    PRIMARY KEY,
    Title       TEXT    NOT NULL,
    HighlightType TEXT  NOT NULL
                        CHECK (HighlightType IN ('OnThisDay','Trip','Event','AIAlbum')),
    StartDate   TEXT,
    EndDate     TEXT,
    MediaIds    TEXT    NOT NULL,                -- JSON array of Media.Id
    CoverMediaId TEXT   REFERENCES Media(Id),
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now','utc'))
);

-- ── 14. GRAPH NODES ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS GraphNodes (
    Id          TEXT    PRIMARY KEY,
    NodeType    TEXT    NOT NULL
                        CHECK (NodeType IN ('Photo','Tag','Location','Event','Album')),
    Label       TEXT    NOT NULL,
    Metadata    TEXT,
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now','utc')),
    UpdatedAt   TEXT    NOT NULL DEFAULT (datetime('now','utc'))
);
CREATE INDEX IF NOT EXISTS idx_gnodes_type  ON GraphNodes(NodeType);
CREATE INDEX IF NOT EXISTS idx_gnodes_label ON GraphNodes(Label);

-- ── 15. GRAPH EDGES ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS GraphEdges (
    FromId      TEXT    NOT NULL REFERENCES GraphNodes(Id) ON DELETE CASCADE,
    ToId        TEXT    NOT NULL REFERENCES GraphNodes(Id) ON DELETE CASCADE,
    EdgeType    TEXT    NOT NULL
                        CHECK (EdgeType IN ('hasTag','takenAt','partOf','relatedTo','near')),
    Weight      REAL    NOT NULL DEFAULT 1.0,
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now','utc')),
    PRIMARY KEY (FromId, ToId, EdgeType)
);
CREATE INDEX IF NOT EXISTS idx_gedges_from ON GraphEdges(FromId);
CREATE INDEX IF NOT EXISTS idx_gedges_to   ON GraphEdges(ToId);
CREATE INDEX IF NOT EXISTS idx_gedges_type ON GraphEdges(EdgeType);
