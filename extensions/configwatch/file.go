package configwatch

import (
	"context"
	"fmt"
	"os"
)

// fileReader reads a Source.Path from the filesystem. A missing file is
// reported as ErrSourceNotFound (wrapped) so the Adapter applies delete
// semantics.
type fileReader struct{}

func newFileReader() Reader { return &fileReader{} }

func (r *fileReader) Read(ctx context.Context, source Source) ([]byte, error) {
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	data, err := os.ReadFile(source.Path)
	if err != nil {
		if os.IsNotExist(err) {
			return nil, fmt.Errorf("%w: %q", ErrSourceNotFound, source.Path)
		}
		return nil, fmt.Errorf("read %q: %w", source.Path, err)
	}
	return data, nil
}
