# RMVC framework code review

Before editing RMVC framework code, present the complete proposed framework diff
to the development lead and wait for explicit approval. Include every changed
framework file and every added, removed or modified line. Identify the base
revision. Present each change directly in the chat response in paired
**Before** and **After** code blocks. Include every changed framework line and
enough unchanged context to show where it belongs. Label the file and use the
source language for each block. A linked document or unified diff can supplement
the chat review. The development lead must be able to review the full change in
the chat itself.
A summary, an architectural agreement or permission to change a consuming
application does not approve the framework diff. If a later revision changes
any lines, present the revised diff and wait for approval before applying it.
Draft proposed code outside the framework source files until it is approved.

Summarise routine test changes and the behaviours they verify. The development
lead can request the test diff when it warrants a closer review. Discuss new
test infrastructure when the development lead requests it.

See [the review method](Doc/Review-method.md#approve-the-exact-framework-diff).

# Release preflight

Start every release by asking the maintainer to confirm the RMVC checkout is up
to date and all intended changes are saved and committed. Wait for confirmation
before preparing or publishing the release. Follow [the release procedure](RELEASING.md#confirm-the-checkout-first).
