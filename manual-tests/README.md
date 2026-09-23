# Manual Test Data

Sample CSV files for exercising the organizer import flow by hand. They contain synthetic
guests only; every address uses the reserved `example.test` domain and is never deliverable.

| File | Purpose |
| --- | --- |
| `import.csv` | Minimal valid import with the four supported columns. |
| `capacity.csv` | Diacritics and an embedded newline inside a quoted field. |
| `phase3.csv` | Extra unsupported columns (`phone`, `priority`) that the parser must ignore. |

`resend.csv` is git-ignored. Create it locally with your own verified addresses when testing
real email dispatch, so live recipients never enter the repository.
