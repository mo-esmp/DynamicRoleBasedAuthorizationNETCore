// Dynamic Authorization dashboard. Loaded in <head> so the theme is applied before the first paint.
(function () {
  "use strict";

  var themeKey = "dynamic-authorization:theme";
  var themes = ["system", "light", "dark"];
  var themeNames = { system: "System theme", light: "Light theme", dark: "Dark theme" };

  function readTheme() {
    try {
      var stored = window.localStorage.getItem(themeKey);
      return themes.indexOf(stored) >= 0 ? stored : "system";
    } catch (e) {
      return "system";
    }
  }

  function applyTheme(theme) {
    document.documentElement.setAttribute("data-theme", theme);
  }

  applyTheme(readTheme());

  function ready(callback) {
    if (document.readyState === "loading") {
      document.addEventListener("DOMContentLoaded", callback);
    } else {
      callback();
    }
  }

  function each(list, callback) {
    Array.prototype.forEach.call(list, callback);
  }

  function initThemeToggle() {
    each(document.querySelectorAll("[data-theme-toggle]"), function (button) {
      function describe() {
        var current = document.documentElement.getAttribute("data-theme") || "system";
        button.title = themeNames[current] + " (click to change)";
        button.setAttribute("aria-label", button.title);
      }

      describe();
      button.addEventListener("click", function () {
        var current = document.documentElement.getAttribute("data-theme") || "system";
        var next = themes[(themes.indexOf(current) + 1) % themes.length];
        applyTheme(next);
        try {
          window.localStorage.setItem(themeKey, next);
        } catch (e) {
          // Storage can be unavailable (private mode); the theme still applies to this page.
        }
        describe();
      });
    });
  }

  // Filters the [data-row] rows of a [data-filter-scope] by text and by <select data-filter-field="x"> values,
  // which are compared with each row's data-x attribute.
  function initFilters() {
    each(document.querySelectorAll("[data-filter-scope]"), function (scope) {
      var text = scope.querySelector("[data-filter-text]");
      var selects = scope.querySelectorAll("[data-filter-field]");
      var rows = scope.querySelectorAll("[data-row]");
      var count = scope.querySelector("[data-filter-count]");
      var empty = scope.querySelector("[data-filter-empty]");

      function apply() {
        var query = text ? text.value.trim().toLowerCase() : "";
        var visible = 0;
        each(rows, function (row) {
          var show = !query || (row.getAttribute("data-search") || row.textContent).toLowerCase().indexOf(query) >= 0;
          each(selects, function (select) {
            if (show && select.value) {
              show = row.getAttribute("data-" + select.getAttribute("data-filter-field")) === select.value;
            }
          });
          row.hidden = !show;
          if (show) {
            visible++;
          }
        });

        if (count) {
          count.textContent = visible === rows.length ? rows.length + " total" : visible + " of " + rows.length;
        }
        if (empty) {
          empty.hidden = visible !== 0;
        }
      }

      if (text) {
        text.addEventListener("input", apply);
      }
      each(selects, function (select) {
        select.addEventListener("change", apply);
      });
      apply();
    });
  }

  function initPermissionTrees() {
    each(document.querySelectorAll("[data-permission-tree]"), function (tree) {
      var boxes = tree.querySelectorAll("input[data-permission]");
      var groups = tree.querySelectorAll("[data-group]");
      var filter = tree.querySelector("[data-tree-filter]");
      var selected = tree.querySelector("[data-selected-count]");
      var noMatches = tree.querySelector("[data-tree-empty]");
      var form = tree.closest("form");
      var changes = form ? form.querySelector("[data-changes]") : null;
      var initial = {};

      each(boxes, function (box) {
        initial[box.value.toLowerCase()] = box.checked;
      });

      function syncGroup(group) {
        var items = group.querySelectorAll("input[data-permission]");
        var checked = 0;
        each(items, function (item) {
          if (item.checked) {
            checked++;
          }
        });

        var toggle = group.querySelector("input[data-group-toggle]");
        toggle.checked = items.length > 0 && checked === items.length;
        toggle.indeterminate = checked > 0 && checked < items.length;
        var counter = group.querySelector("[data-group-count]");
        if (counter) {
          counter.textContent = checked + " / " + items.length;
        }
      }

      function summarize() {
        var total = 0;
        var added = 0;
        var removed = 0;
        each(boxes, function (box) {
          var wasChecked = initial[box.value.toLowerCase()];
          if (box.checked) {
            total++;
          }
          if (box.checked && !wasChecked) {
            added++;
          }
          if (!box.checked && wasChecked) {
            removed++;
          }
        });

        if (selected) {
          selected.textContent = total + " of " + boxes.length + " granted";
        }
        if (changes) {
          changes.textContent = "";
          if (added === 0 && removed === 0) {
            changes.textContent = "No unsaved changes";
          } else {
            var plus = document.createElement("span");
            plus.className = "da-added";
            plus.textContent = "+" + added;
            var minus = document.createElement("span");
            minus.className = "da-removed";
            minus.textContent = "−" + removed;
            changes.appendChild(plus);
            changes.appendChild(document.createTextNode(" "));
            changes.appendChild(minus);
            changes.appendChild(document.createTextNode(" unsaved changes"));
          }
        }
      }

      function visibleBoxes(scope) {
        return Array.prototype.filter.call(scope.querySelectorAll("input[data-permission]"), function (box) {
          return !box.closest("[hidden]");
        });
      }

      function setVisible(scope, checked) {
        visibleBoxes(scope).forEach(function (box) {
          box.checked = checked;
        });
        each(groups, syncGroup);
        summarize();
      }

      each(groups, function (group) {
        var toggle = group.querySelector("input[data-group-toggle]");
        // The toggle sits inside <summary>; keep clicks from also opening or closing the group.
        toggle.addEventListener("click", function (event) {
          event.stopPropagation();
        });
        toggle.addEventListener("change", function () {
          setVisible(group, toggle.checked);
        });
        group.addEventListener("change", function (event) {
          if (event.target.hasAttribute("data-permission")) {
            syncGroup(group);
            summarize();
          }
        });
        syncGroup(group);
      });

      if (filter) {
        filter.addEventListener("input", function () {
          var query = filter.value.trim().toLowerCase();
          var anyVisible = false;
          each(groups, function (group) {
            var groupName = (group.getAttribute("data-group") || "").toLowerCase();
            var groupMatches = query !== "" && groupName.indexOf(query) >= 0;
            var visibleItems = 0;
            each(group.querySelectorAll("[data-item]"), function (item) {
              var show = !query || groupMatches || item.getAttribute("data-search").toLowerCase().indexOf(query) >= 0;
              item.hidden = !show;
              if (show) {
                visibleItems++;
              }
            });
            group.hidden = visibleItems === 0;
            if (query && visibleItems > 0) {
              group.open = true;
            }
            anyVisible = anyVisible || visibleItems > 0;
          });
          if (noMatches) {
            noMatches.hidden = anyVisible;
          }
        });
      }

      each(tree.querySelectorAll("[data-tree-action]"), function (button) {
        button.addEventListener("click", function () {
          var action = button.getAttribute("data-tree-action");
          if (action === "expand" || action === "collapse") {
            each(groups, function (group) {
              group.open = action === "expand";
            });
          } else {
            setVisible(tree, action === "select-all");
          }
        });
      });

      summarize();
    });
  }

  // Forms with data-confirm ask for confirmation in a dialog before submitting.
  function initConfirmations() {
    var dialog = document.getElementById("da-confirm");
    if (!dialog || typeof dialog.showModal !== "function") {
      return;
    }

    document.addEventListener("submit", function (event) {
      var form = event.target;
      var message = form.getAttribute("data-confirm");
      if (!message || form.getAttribute("data-confirmed") === "true") {
        return;
      }

      event.preventDefault();
      dialog.querySelector("[data-confirm-title]").textContent = form.getAttribute("data-confirm-title") || "Are you sure?";
      dialog.querySelector("[data-confirm-message]").textContent = message;
      dialog.querySelector("[data-confirm-accept]").textContent = form.getAttribute("data-confirm-button") || "Confirm";
      dialog.returnValue = "";
      dialog.addEventListener("close", function onClose() {
        dialog.removeEventListener("close", onClose);
        if (dialog.returnValue === "confirm") {
          form.setAttribute("data-confirmed", "true");
          form.submit();
        }
      });
      dialog.showModal();
    });
  }

  ready(function () {
    initThemeToggle();
    initFilters();
    initPermissionTrees();
    initConfirmations();
  });
})();
