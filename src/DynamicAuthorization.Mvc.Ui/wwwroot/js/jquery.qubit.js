(function($) {
  $.fn.qubit = function(options) {
    return this.each(function() {
      new Qubit(this, options);
    });
  };
  var Qubit = function(el) {
    var self = this;
    this.scope = $(el);
    var handler = function(e) {
      if (!self.suspendListeners) {
        self.process(e.target);
      }
    };
    this.scope.on('change', 'input[type=checkbox]', handler);
    // workaround for IE<10
    if (document.documentMode && document.documentMode <= 9) {
      this.scope.on('click', 'input[type=checkbox]:indeterminate', handler);
    }
    this.processParents();
  };
  Qubit.prototype = {
    itemSelector: 'li',
    process: function(checkbox) {
      checkbox = $(checkbox);
      var parentItems = checkbox.parentsUntil(this.scope, this.itemSelector);
      var self = this;
      try {
        this.suspendListeners = true;
        // all children inherit my state
        parentItems.eq(0).find('input[type=checkbox]').not(':disabled')
          .filter(checkbox.prop('checked') ? ':not(:checked)' : ':checked')
          .each(function() {
            if (!$(this).parent().hasClass('hidden')) {
              self.setChecked($(this), checkbox.prop('checked'));
            }
          })
          .trigger('change');
        this.processParents();
      } finally {
        this.suspendListeners = false;
      }
    },
    processParents: function() {
      var self = this, changed = false;
      this.scope.find('input[type=checkbox]').not(':disabled').each(function() {
        var $this = $(this);
        var parent = $this.closest(self.itemSelector);
        var children = parent.find('input[type=checkbox]').not(':disabled').not($this);
        var numChecked = children.filter(function() {
          return $(this).prop('checked') || $(this).prop('indeterminate');
        }).length;

        if (children.length) {
          if (numChecked === 0) {
            self.setChecked($this, false) && (changed = true);
          }
          else if (numChecked == children.length) {
            self.setChecked($this, true) && (changed = true);
          }
          else {
            self.setIndeterminate($this, true) && (changed = true);
          }
        }
        else {
          self.setIndeterminate($this, false) && (changed = true);
        }
      });
      if (changed) this.processParents();
    },
    setChecked: function(checkbox, value, event) {
      var changed = false;
      if (checkbox.prop('indeterminate')) {
        checkbox.prop('indeterminate', false);
        changed = true;
      }
      if (checkbox.prop('checked') != value) {
        checkbox.prop('checked', value).trigger('change');
        changed = true;
      }
      return changed;
    },
    setIndeterminate: function(checkbox, value) {
      if (value) {
        checkbox.prop('checked', false);
      }
      if (checkbox.prop('indeterminate') != value) {
        checkbox.prop('indeterminate', value);
        return true;
      }
    }
  };
}(jQuery));
