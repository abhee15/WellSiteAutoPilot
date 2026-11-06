# Command panel reachability

Fixes #476, after #475. A fixed-height task scrollport used implicit auto grid tracks. Its panels had overflow:hidden, which allowed the grid to shrink them to the available height. The outer scrollport appeared to fit while thousands of pixels of rows were clipped inside a panel. Adding an outer scrollbar alone could not expose that content.

Use max-content grid tracks so each card retains its intrinsic content height and the existing task scrollport owns the overflow. Preserve panel corner clipping, the desktop header/navigation, normal narrow/short-window scrolling and all control permissions and behavior.

The browser regression uses 30 commands and expanded resolution/release forms. It scrolls using wheel, keyboard End/PageDown and Tab, checks the last action against every clipping ancestor, and rejects hidden panels being programmatically scrolled as a workaround. It covers 1440×900, 1000×600, 900×820, 420×850, 320×850, 1440×500 and 1000×650 with 125% root text. No command or control-setting write is submitted.

The regression failed against the original layout: the task scrollport was 727px tall with no overflow, while its inner panel held about 5124px of clipped content and the final action remained below y=5300. With max-content tracks, all 28 before/after cases passed, including last-row wheel reachability, keyboard focus and expanded-form footers. Local build/typecheck, lint, 349 UI tests and 24 focused interaction tests passed. The visual workflow publishes screenshots and JSON geometry; final CI is required before merging.
