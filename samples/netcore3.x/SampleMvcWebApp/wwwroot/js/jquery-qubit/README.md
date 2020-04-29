# jquery-qubit

Provides the semantics for a nested list of tri-state checkboxes, using the HTML5 
"indeterminate" property. When attached to a jQuery element `jquery-qubit`
listens for change events to `input[type=checkbox]` elements, and updates 
the `checked` and `indeterminate` based on the checked values of any checkboxes 
in child elements of the DOM.

This plugin is used by [jQuery Bonsai](https://github.com/aexmachina/jquery-bonsai). For
a full usage example please see there.

## Usage

```
$('ul#the-list').qubit();
```