![Icon](./AppIcon.ico)

# csFind - Revision History

## v1.0.0.0 - August, 2025
**Dependencies**

| Assembly | Version |
| ---- | ---- |
| .NET Framework | 4.0.30319 |

- Initial release of **csFind** utility.
    - This utility searches all `files` in the `drive` that contain a matching `term`
    - Results are logged and output to the console
- Optional command line arguments:
    - Use `--term <value>` to specify a match term (text to search for inside files)
        - You can specify multiple terms using multiple `--term` arguments
    - Use `--drive <value>` to specify a drive term
    - Use `--pattern <value>` to specify a file search pattern term
    - Use `--threads <value>` to specify total thread count term
    - Use `--percent <value>` to specify the amount of terms that need to be discovered for a positive hit
    - Use `--locate` to enable file search mode (files will not be opened and parsed, only matched based on the given pattern)
- Multi-threading mode:
    - In the interest of time, up to 4 threads will be used during the search process.
    - This can be adjusted using the `--threads <value>` command line argument.
    - You don't want to go crazy with the thread count; in most cases throwing more threads at a problem does not make it better, e.g. using 50 threads may cause the process to finish slightly faster but will bring the client machine to its knees. 4 to 8 threads is adequate for most systems.

> Multi-term search mode (similar to Grep)

```bash
  csFind --drive d: --pattern *.log --percent 0.95 --term Banking --term Authorize --term Transaction
  csFind --drive c:\temp --pattern *.log --percent 0.7 --threads 8 --term ssdeep --term warning --term combination --term result
```

> Locate mode examples (similar to Windows Explorer file search):

```bash
  csFind --locate --drive d: --pattern App.config --threads 8
  csFind --locate --drive c:\temp --pattern Debug*.log --threads 8
```
