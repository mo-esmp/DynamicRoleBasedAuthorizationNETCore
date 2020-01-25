# jQuery Bonsai

#### [Visit project page and demos](http://aexmachina.info/jquery-bonsai)

`jquery-bonsai` is a lightweight jQuery plugin that takes a big nested list and prunes it down to a small expandable 
tree control.

Also includes support for checkboxes (including 'indeterminate' state) and for populating the tree using a JSON data source.

See [aexmachina.github.io/jquery-bonsai/](http://aexmachina.github.io/jquery-bonsai/) for more information.

## Installation

```
bower install jquery-bonsai --save
```

## Usage

```
$('ul#my-nested-list').bonsai();
```

## API

### `$.fn.bonsai(options)`

```js
$('#list').bonsai({
  expandAll: false, // expand all items
  expand: null, // optional function to expand an item
  collapse: null, // optional function to collapse an item
  addExpandAll: false, // add a link to expand all items
  addSelectAll: false, // add a link to select all checkboxes
  selectAllExclude: null, // a filter selector or function for selectAll
  idAttribute: 'id', // which attribute of the list items to use as an id

  // createInputs: create checkboxes or radio buttons for each list item
  // using a value of "checkbox" or "radio".
  //
  // The id, name and value for the inputs can be declared in the
  // markup using `data-id`, `data-name` and `data-value`.
  //
  // The name is inherited from parent items if not specified.
  //
  // Checked state can be indicated using `data-checked`.
  createInputs: false,
  // checkboxes: run qubit(this.options) on the root node (requires jquery.qubit)
  checkboxes: false,
  // handleDuplicateCheckboxes: update any other checkboxes that
  // have the same value
  handleDuplicateCheckboxes: false
});
```

### `Bonsai#update()`

If the DOM changes then you'll need to call `#update`:

```js
$('#list').bonsai('update');
```

### `Bonsai#listItem(id)`

Return a jQuery object containing the `<li>` with the specified `id`.

### Expanding/collapsing a single items

- `Bonsai#expand(listItem)`
- `Bonsai#collapse(listItem)`
- `Bonsai#toggle(listItem)`
- `Bonsai#expandTo(listItem)`

```js
var bonsai = $('#list').data('bonsai');
bonsai.expand(listItem);
```

All of these methods accept either a DOMElement, a jQuery object or an `id` and return a 
jQuery object containing the list item.

### Expanding/collapsing the whole tree

- `Bonsai#expandAll(listItem)`
- `Bonsai#collapseAll(listItem)`

### `Bonsai#serialize()`

Returns an object representing the expanded/collapsed state of the list, using the items' id
to identify the list items.

```js
var bonsai = $('#list').data('bonsai');
var state = bonsai.serialize();
```

### `Bonsai#restore()`

Restores the expanded/collapsed state of the list using the return value of `#serialize()`.

```js
var bonsai = $('#list').data('bonsai');
var state = bonsai.serialize();
// do stuff that changes the DOM, and may not retain collapsed state
bonsai.update(); // update to handle any new DOM elements
bonsai.restore(state); // restores the collapsed state
```
