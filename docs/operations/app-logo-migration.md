# Logo personnalisé — migration du schéma

> **Historical.** Already part of `0001_initial.sql`: a database created or updated after schema
> migrations (October 2026) needs nothing from this page.

```sql
CREATE TABLE IF NOT EXISTS app_logo (
  size smallint NOT NULL,
  image mediumblob NOT NULL,
  updated_at datetime(3) NOT NULL,
  PRIMARY KEY (size)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

À rejouer à la main sur chaque base — prod et dev — avant de déployer le code qui la lit : sans
elle, `GET /api/AppSettings` échoue, et avec lui la page de login.

Vérifier ensuite :

```sql
SHOW CREATE TABLE app_logo;
```
