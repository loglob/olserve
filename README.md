# olserve
Serves and caches PDFs compiled via overleaf.
Tested against overleaf versions 5.1 to 6.3. 

## Configuration
Configured via a JSON file like this:
```json
{
	"port": 8803,
	"routes": {
		"/foo.pdf": {
			"link": "https://my-overleaf-instance.com/INVITE_LINK_HERE",
			"mainFile": "cs_new.tex"
		},
		"/bar.pdf": {
			"link": "https://another-overleaf-instance.com/INVITE_LINK_HERE",
			"hooks": { "onCompileFailed": "./hook.sh" }
		}
	}
}
```
> [!IMPORTANT]
> These project join links must have read+write permissions because the history API requires write access.
> The project contents are not modified in any way.

This configures the server to run on port 8803 and serve these two files by querying the listed overleaf servers for updates and compiled PDFs.
Routes may be configured with these optional fields:

**mainFile** Specifies a path in the project to use as compilation entrypoint.

**hooks** specifies some script hooks to run.
Currently, only `onCompileFailed` is supported.
Hooks are specified either as paths to scripts, or an array of a program and arguments like `[ "script.sh", "arg1", "arg2" ]`.

The path to the config file defaults to `./config.json` and can be passed as program argument.

## Hooks
Hooks are regular executables invoked by olserve.
They receive the preconfigured arguments, and some environment variables controlled by olserve:
| Variable | Description |
|----------|-------------|
| ROUTE    | The relevant HTTP route, exactly as specified in the config. |
| MAIN_FILE | The `mainFile` configuration value, if one was given. Unset otherwise. |
| DELAYED | This flag is set to `true` if a previous invocation of the hook was still running while the hook was fired a second time. In that case, the second invocation is delayed until the first finishes. Unset otherwise.  |
| LOGS | The compiler warnings received on compile failure. |
| PERSISTENT | Set to `true` if the error indicates a persistent issue with the project code, rather than a transient service issue. Unset otherwise. |
| STATUS | The status flag returned by the overleaf API, e.g. `stopped-on-first-error`, `too-recently-compiled`. |
| REVISION_TIME | A whole-seconds unix timestamp of the latest edit of the project. May be unset if that information is not available. |
| LAST_GOOD_REVISION | A whole-seconds unix timestamp of the last revision that compiled successfully. May be unset. |
