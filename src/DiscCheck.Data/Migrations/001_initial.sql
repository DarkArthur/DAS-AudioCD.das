CREATE TABLE schema_version (version INTEGER NOT NULL);

CREATE TABLE drives (
  id INTEGER PRIMARY KEY, vendor TEXT NOT NULL, model TEXT NOT NULL, firmware TEXT NOT NULL,
  c2_supported INTEGER, read_offset INTEGER,
  caches_audio INTEGER, cache_kb INTEGER, c2_reliable INTEGER,
  UNIQUE (vendor, model, firmware));

CREATE TABLE media (
  id INTEGER PRIMARY KEY, media_id TEXT, manufacturer TEXT, type TEXT, label TEXT);

CREATE TABLE discs (
  id INTEGER PRIMARY KEY, label TEXT, media_id INTEGER REFERENCES media(id),
  burn_drive TEXT, burn_speed REAL, burn_date TEXT, burn_software TEXT,
  toc_hash TEXT NOT NULL, track_count INTEGER NOT NULL, total_sectors INTEGER NOT NULL,
  storage_notes TEXT, recheck_due TEXT);

CREATE TABLE scans (
  id INTEGER PRIMARY KEY, disc_id INTEGER NOT NULL REFERENCES discs(id),
  drive_id INTEGER NOT NULL REFERENCES drives(id),
  scanned_at TEXT NOT NULL, speed_x REAL NOT NULL,
  scanner TEXT NOT NULL CHECK (scanner IN ('StandardC2','LiteOn')),
  verdict TEXT NOT NULL CHECK (verdict IN ('OK','WARN','KO')),
  c1_total INTEGER, c1_mean REAL, c1_max INTEGER, c2_total INTEGER, c2_max INTEGER, cu_total INTEGER);

CREATE TABLE scan_samples (
  scan_id INTEGER NOT NULL REFERENCES scans(id) ON DELETE CASCADE,
  t_sec INTEGER NOT NULL, lba INTEGER NOT NULL,
  c1 INTEGER, c2 INTEGER NOT NULL, cu INTEGER NOT NULL, speed_x REAL,
  PRIMARY KEY (scan_id, t_sec)) WITHOUT ROWID;

CREATE TABLE verifications (
  id INTEGER PRIMARY KEY, disc_id INTEGER NOT NULL REFERENCES discs(id),
  drive_id INTEGER NOT NULL REFERENCES drives(id),
  verified_at TEXT NOT NULL, source_path TEXT, offset_used INTEGER NOT NULL,
  result TEXT NOT NULL CHECK (result IN ('IDENTICAL','DIFFERENT','ERROR')));

CREATE TABLE verification_tracks (
  verification_id INTEGER NOT NULL REFERENCES verifications(id) ON DELETE CASCADE,
  track_no INTEGER NOT NULL, identical INTEGER NOT NULL, different_samples INTEGER NOT NULL,
  first_diff_index INTEGER, crc32 TEXT NOT NULL, sha256 TEXT NOT NULL,
  excluded_edge_samples INTEGER NOT NULL, unreliable_lbas TEXT,
  PRIMARY KEY (verification_id, track_no));
