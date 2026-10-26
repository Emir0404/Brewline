// Ask before dangerous actions: <form data-confirm="Cancel this order?"> ...
document.addEventListener('submit', function (event) {
    var message = event.target.dataset ? event.target.dataset.confirm : null;
    if (message && !window.confirm(message)) {
        event.preventDefault();
    }
});
