import logging
import signal
import threading

_STOP = threading.Event()


def _request_stop(signum: int, _frame: object) -> None:
    logging.getLogger(__name__).info("Stop requested by signal %s.", signum)
    _STOP.set()


def main() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s %(message)s",
    )

    signal.signal(signal.SIGINT, _request_stop)
    signal.signal(signal.SIGTERM, _request_stop)

    logging.getLogger(__name__).info("WellSite AutoPilot Python Worker started.")
    _STOP.wait()


if __name__ == "__main__":
    main()
