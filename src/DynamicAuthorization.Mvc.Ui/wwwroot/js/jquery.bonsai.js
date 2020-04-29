(function($){
  $.fn.bonsai = function(options) {
    var args = arguments;
    return this.each(function() {
      var bonsai = $(this).data('bonsai');
      if (!bonsai) {
        bonsai = new Bonsai(this, options);
        $(this).data('bonsai', bonsai);
      }
      if (typeof options == 'string') {
        var method = options;
        return bonsai[method].apply(bonsai, [].slice.call(args, 1));
      }
    });
  };
  $.bonsai = {};
  $.bonsai.defaults = {
    expandAll: false, // expand all items
    expand: null, // optional function to expand an item
    collapse: null, // optional function to collapse an item
    addExpandAll: false, // add a link to expand all items
    addSelectAll: false, // add a link to select all checkboxes
    selectAllExclude: null, // a filter selector or function for selectAll
    idAttribute: 'id', // which attribute of the list items to use as an id

    // createInputs: create checkboxes or radio buttons for each list item
    // by setting createInputs to "checkbox" or "radio".
    //
    // The name and value for the inputs can be declared in the
    // markup using `data-name` and `data-value`.
    //
    // The name is inherited from parent items if not specified.
    //
    // Checked state can be indicated using `data-checked`.
    createInputs: false,
    // checkboxes: run qubit(this.options) on the root node (requires jquery.qubit)
    checkboxes: false,
    // handleDuplicateCheckboxes: update any other checkboxes that
    // have the same value
    handleDuplicateCheckboxes: false,
    // createRadioButtons: creates radio buttons for each list item.
    //
    // The name and value for the checkboxes can be declared in the
    // markup using `data-name` and `data-value`.
    //
    // The name is inherited from parent items if not specified.
    //
    // Checked state can be indicated using `data-checked`.
    createRadioButtons: false
  };
  var Bonsai = function(el, options) {
    var self = this;
    options = options || {};
    this.options = $.extend({}, $.bonsai.defaults, options);
    this.el = $(el).addClass('bonsai').data('bonsai', this);

    // store the scope in the options for child nodes
    if (!this.options.scope) {
      this.options.scope = this.el;
    }
    this.update();
    if (this.isRootNode()) {
      if (this.options.createCheckboxes) this.createInputs = 'checkbox';
      if (this.options.handleDuplicateCheckboxes) this.handleDuplicateCheckboxes();
      if (this.options.checkboxes) this.el.qubit(this.options);
      if (this.options.addExpandAll) this.addExpandAllLink();
      if (this.options.addSelectAll) this.addSelectAllLink();
      this.el.on('click', '.thumb', function(ev) {
        self.toggle($(ev.currentTarget).closest('li'));
      });
    }
    if (this.options.expandAll) this.expandAll();
  };
  Bonsai.prototype = {
    isRootNode: function() {
      return this.options.scope == this.el;
    },
    listItem: function(id) {
      if (typeof id === 'object') return $(id);
      return this.el.find('[' + this.options.idAttribute + '="' + id + '"]');
    },
    toggle: function(listItem) {
      if (!$(listItem).hasClass('expanded')) {
        return this.expand(listItem);
      }
      else {
        return this.collapse(listItem);
      }
    },
    expand: function(listItem) {
      return this.setExpanded(listItem, true);
    },
    collapse: function(listItem) {
      return this.setExpanded(listItem, false);
    },
    setExpanded: function(listItem, expanded) {
      var $li = this.listItem(listItem);
      if ($li.length > 1) {
        var self = this;
        $li.each(function() {
          self.setExpanded(this, expanded);
        });
        return;
      }
      if (expanded) {
        if (!$li.data('subList')) return;
        $li = $($li).addClass('expanded').removeClass('collapsed');
        $($li.data('subList')).css('height', 'auto');
      }
      else {
        $li = $($li).addClass('collapsed')
          .removeClass('expanded');
        $($li.data('subList')).height(0);
      }
      return $li;
    },
    expandAll: function() {
      this.expand(this.el.find('li'));
    },
    collapseAll: function() {
      this.collapse(this.el.find('li'));
    },
    expandTo: function(listItem) {
      var self = this;
      var $li = this.listItem(listItem);
      $li.parents('li').each(function () {
        self.expand($(this));
      });
      return $li;
    },
    update: function() {
      var self = this;
      // look for a nested list (if any)
      this.el.children().each(function() {
        var item = $(this);
        if (self.options.createInputs) self.insertInput(item);

        // insert a thumb if it doesn't already exist
        if (item.children().filter('.thumb').length == 0) {
          var thumb = $('<div class="thumb"></div>');
          item.prepend(thumb);
        }
        var subLists = item.children().filter('ol, ul');
        item.toggleClass('has-children', subLists.find('li').length > 0);
        // if there is a child list
        subLists.each(function() {
          // that's not empty
          if ($('li', this).length == 0) {
            return;
          }
          // then this el has children
          item.data('subList', this);
          // collapse the nested list
          if (item.hasClass('expanded')) {
            self.expand(item);
          }
          else {
            self.collapse(item);
          }
          // handle any deeper nested lists
          var exists = !!$(this).data('bonsai');
          $(this).bonsai(exists ? 'update' : self.options);
        });
      });

      this.expand = this.options.expand || this.expand;
      this.collapse = this.options.collapse || this.collapse;
    },
    serialize: function() {
      var idAttr = this.options.idAttribute;
      return this.el.find('li').toArray().reduce(function(acc, li) {
        var $li = $(li);
        var id = $li.attr(idAttr);
        // only items with IDs can be serialized
        if (id) {
          var state = $li.hasClass('expanded')
              ? 'expanded'
              : ($li.hasClass('collapsed') ? 'collapsed' : null);
          if (state) acc[$li.hasClass('expanded') ? 'expanded' : 'collapsed'].push(id);
        }
        return acc;
      }, {expanded: [], collapsed: [], version: 2});
    },
    restore: function(state) {
      var self = this;
      if (state.version > 1) {
        state.expanded.map(this.expand.bind(this));
        state.collapsed.map(this.collapse.bind(this));
      }
      else {
        Object.keys(state).forEach(function(id) {
          self.setExpanded(id, state[id] === 'expanded');
        });
      }
    },
    insertInput: function(listItem) {
      var type = this.options.createInputs;
      if (listItem.find('> input[type=' + type + ']').length) return;
      var id = this.inputIdFor(listItem);
      var checkbox = $('<input type="' + type + '" name="'
        + this.inputNameFor(listItem) + '" id="' + id + '" /> '
      );
      var children = listItem.children();
      // get the first text node for the label
      var text = listItem.contents().filter(function() {
        return this.nodeType == 3;
      }).first();
      checkbox.val(listItem.data('value'));
      checkbox.prop('checked', listItem.data('checked'))
      children.detach();
      listItem.append(checkbox)
        .append(
          $('<label for="' + id + '">').append(text.length > 0 ? text : children.first())
        )
        .append(text.length > 0 ? children : children.slice(1));
    },
    checkboxPrefix: 'bonsai-checkbox-',
    inputIdFor: function(listItem) {
      var id = $(listItem).data('id');
      while (!id || ($('#' + id).length > 0)) {
        id = this.checkboxPrefix + Bonsai.uniqueId++;
      }
      return id;
    },
    inputNameFor: function(listItem) {
      return listItem.data('name')
        || listItem.parents().filter('[data-name]').data('name');
    },
    handleDuplicateCheckboxes: function() {
      var self = this;
      self.el.on('change', 'input[type=checkbox]', function(ev) {
        var checkbox = $(ev.target);
        if (!checkbox.val()) return;
        // select all duplicate checkboxes that need to be updated
        var selector = 'input[type=checkbox]'
          + '[value="' + checkbox.val() + '"]'
          + (checkbox.attr('name') ? '[name="' + checkbox.attr('name') + '"]' : '')
          + (checkbox.prop('checked') ? ':not(:checked)' : ':checked');
        self.el.find(selector).prop({
          checked: checkbox.prop('checked'),
          indeterminate: checkbox.prop('indeterminate')
        }).trigger('change');
      });
    },
    addExpandAllLink: function() {
      var self = this;
      $('<div class="expand-all">')
        .append(
          $('<a class="all">Expand all</a>').on('click', function() {
            self.expandAll();
          })
        )
        .append('<i class="separator"></i>')
        .append(
          $('<a class="none">Collapse all</a>').on('click', function() {
            self.collapseAll();
          })
        )
        .insertBefore(this.el);
    },
    addSelectAllLink: function() {
      var scope = this.options.scope;
      var self = this;
      function getCheckboxes() {
        // return all checkboxes that are not in hidden list items
        return scope.find('li')
          .filter(self.options.selectAllExclude || function() {
            return $(this).css('display') != 'none';
          })
          .find('> input[type=checkbox]');
      }
      $('<div class="check-all">')
        .append($('<a class="all">Select all</a>')
          .css('cursor', 'pointer')
          .on('click', function() {
            getCheckboxes().prop({
              checked: true,
              indeterminate: false
            });
          }))
        .append('<i class="separator"></i>')
        .append($('<a class="none">Select none</a>')
          .css('cursor', 'pointer')
          .on('click', function() {
            getCheckboxes().prop({
              checked: false,
              indeterminate: false
            });
          })
      )
        .insertAfter(this.el);
    },
    setCheckedValues: function(values) {
      var all = this.options.scope.find('input[type=checkbox]');
      $.each(values, function(key, value) {
        all.filter('[value="' + value + '"]')
          .prop('checked', true)
          .trigger('change');
      });
    }
  };
  $.extend(Bonsai, {
    uniqueId: 0
  });
}(jQuery));
