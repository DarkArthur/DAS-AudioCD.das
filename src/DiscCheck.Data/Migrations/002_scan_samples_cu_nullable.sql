-- CU nullable: NULL = "non disponibile" (Metodo_Nuovo Lite-On non misura E32). Vedi docs/liteon-cdb.md.
-- SQLite non permette di rimuovere NOT NULL da una colonna: si ricostruisce la tabella.
-- I dati esistenti sono copiati senza modifiche (i valori 0 restano 0).
CREATE TABLE scan_samples_new (
  scan_id INTEGER NOT NULL REFERENCES scans(id) ON DELETE CASCADE,
  t_sec INTEGER NOT NULL, lba INTEGER NOT NULL,
  c1 INTEGER, c2 INTEGER NOT NULL, cu INTEGER, speed_x REAL,
  PRIMARY KEY (scan_id, t_sec)) WITHOUT ROWID;

INSERT INTO scan_samples_new (scan_id, t_sec, lba, c1, c2, cu, speed_x)
  SELECT scan_id, t_sec, lba, c1, c2, cu, speed_x FROM scan_samples;

DROP TABLE scan_samples;
ALTER TABLE scan_samples_new RENAME TO scan_samples;
